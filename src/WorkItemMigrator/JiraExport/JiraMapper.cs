﻿using Common.Config;
using HtmlAgilityPack;
using Migration.Common;
using Migration.Common.Config;
using Migration.Common.Log;
using Migration.WIContract;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Migration.Jira-Export.Tests")]

namespace JiraExport
{
    internal class JiraMapper : BaseMapper<JiraRevision>
    {
        private readonly IJiraProvider _jiraProvider;
        private readonly Dictionary<string, FieldMapping<JiraRevision>> _fieldMappingsPerType;
        private readonly HashSet<string> _targetTypes;
        private readonly ConfigJson _config;
        private readonly ExportIssuesSummary exportIssuesSummary;

        public JiraMapper(IJiraProvider jiraProvider, ConfigJson config, ExportIssuesSummary exportIssuesSummary)
            : base(jiraProvider?.GetSettings()?.UserMappingFile)
        {
            _jiraProvider = jiraProvider;
            _config = config;
            _targetTypes = InitializeTypeMappings();
            _fieldMappingsPerType = InitializeFieldMappings(exportIssuesSummary);
            this.exportIssuesSummary = exportIssuesSummary;
        }

        #region Mapping definitions

        internal WiItem Map(JiraItem issue)
        {
            if (issue == null)
                throw new ArgumentNullException(nameof(issue));

            var wiItem = new WiItem();

            if (_config.TypeMap.Types != null)
            {
                var type = (from t in _config.TypeMap.Types where t.Source == issue.Type select t.Target).FirstOrDefault();

                if (type != null)
                {
                    var revisions = issue.Revisions.Select(r => MapRevision(r)).ToList();
                    revisions = CollapseRevisions(revisions);

                    wiItem.OriginId = issue.Key;
                    wiItem.Type = type;
                    wiItem.Revisions = revisions;
                }
                else
                {
                    Logger.Log(LogLevel.Error, $"Type mapping missing for '{issue.Key}' with Jira type '{issue.Type}'. Item was not exported which may cause missing links in issues referencing this item.");
                    exportIssuesSummary.AddUnmappedIssueType(issue.Type);
                    return null;
                }
            }

            return wiItem;
        }

        private List<WiRevision> CollapseRevisions(List<WiRevision> revisions)
        {
            var nonEmptyRevisions = new List<WiRevision>();

            foreach (var revision in revisions)
            {
                foreach (var field in revision.Fields.Where(f =>
                             f.ReferenceName == WiFieldReference.Description ||
                             f.ReferenceName == WiFieldReference.History ||
                             f.ReferenceName == WiFieldReference.ReproSteps))
                {
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(field.Value as string);
                    var inlineImages = htmlDoc.DocumentNode.SelectNodes("//img");
                    if (inlineImages == null) continue;

                    var previousAttachments = revisions
                        .Where(r => r.Index < revision.Index)
                        .SelectMany(r => r.Attachments)
                        .Select(a => a.FileName)
                        .ToHashSet();

                    var nextAttachments = revisions
                        .Where(r => r.Index > revision.Index)
                        .SelectMany(r => r.Attachments)
                        .Select(a => a.FileName)
                        .ToHashSet();

                    ReplaceInlineImages(inlineImages, revision, field, previousAttachments, nextAttachments);
                }

                if (revision.Fields.Any() || revision.Attachments.Any() || revision.Links.Any() || revision.DevelopmentLink != null)
                {
                    nonEmptyRevisions.Add(revision);
                }
            }

            var revisionIndex = 0;
            var oldTime = DateTime.MinValue;
            foreach (var revision in nonEmptyRevisions)
            {
                revision.Index = revisionIndex++;
                if (revision.Time <= oldTime)
                    revision.Time += TimeSpan.FromMilliseconds(50);
                oldTime = revision.Time;
            }

            return nonEmptyRevisions;

            void ReplaceInlineImages(HtmlNodeCollection inlineImages, WiRevision revision, WiField field,
                HashSet<string> previousAttachments, HashSet<string> nextAttachments)
            {
                foreach (var inlineImage in inlineImages)
                {
                    var imageSourceAttribute = inlineImage.GetAttributeValue("src", string.Empty);
                    var imageUri = new Uri(imageSourceAttribute);
                    if (imageUri.Host == "orphanattachment.internal")
                    {
                        Logger.Log(LogLevel.Warning, $"Inline image '{imageUri.LocalPath}' in revision {revision.Index} is an orphan attachment.");
                        var attachmentId = (from att in _config.OrphanAttachments where "/" + att.Filename == imageUri.AbsolutePath select att.AttachmentId).FirstOrDefault();
                        if (attachmentId != 0)
                        {
                            var attachmentUrl = DownloadSingleAttachment(attachmentId.ToString(), imageUri.AbsolutePath[1..], revision);
                            field.Value = (field.Value as string)?.Replace(imageSourceAttribute, attachmentUrl);
                        }
                        else
                        {
                            var message = $"Orphan attachment '{imageUri.LocalPath}' in revision {revision.ParentOriginId}.{revision.Index} is not found in the configuration.";
                            Logger.Log(LogLevel.Error, message);
                            // throw new Exception(message);
                        }
                        continue;
                    }
                    var imageLocalPath = imageUri.LocalPath;
                    var imageFilename = Path.GetFileName(imageLocalPath);
                    var imageFolderName = Path.GetFileName(Path.GetDirectoryName(imageLocalPath));
                    var imageFilenameAlternate = Regex.Replace(imageFilename, "^" + Regex.Escape(imageFolderName ?? string.Empty) + "_", "");

                    // Skip if the image is already an attachment
                    if (previousAttachments.Contains(imageFilename) || previousAttachments.Contains(imageFilenameAlternate)) continue;

                    // Collapse next revisions if the image is an attachment
                    if (nextAttachments.Contains(imageFilename) || nextAttachments.Contains(imageFilenameAlternate))
                    {
                        // Move the attachment to the current revision
                        var attachmentToMove = revisions
                            .Where(r => r.Index > revision.Index)
                            .SelectMany(r => r.Attachments)
                            .FirstOrDefault(a => a.FileName == imageFilename || a.FileName == imageFilenameAlternate);

                        if (attachmentToMove == null) continue;

                        revision.Attachments.Add(attachmentToMove);
                        revision.AttachmentReferences = true;
                        revisions
                            .Where(r => r.Index > revision.Index)
                            .ToList()
                            .ForEach(r => r.Attachments.Remove(attachmentToMove));
                    }
                    else
                    {
                        DownloadSingleAttachment(imageFolderName, imageFilename, revision);
                    }
                }
            }
            string DownloadSingleAttachment(string imageFolderName, string imageFilename, WiRevision revision)
            {
                // Download the image manually and add it as an attachment (on this revision)
                int jiraAttachmentId;
                try
                {
                    jiraAttachmentId = int.Parse(imageFolderName);
                }
                catch (FormatException)
                {
                    Logger.Log(LogLevel.Error, $"Inline image '{imageFilename}' in revision {revision.ParentOriginId}.{revision.Index} is not an attachment.");
                    return null;
                }

                var jiraAttachment = _jiraProvider.DownloadAttachment(jiraAttachmentId).Result;

                if (jiraAttachment != null)
                {
                    var attachment = new WiAttachment()
                    {
                        Change = ReferenceChangeType.Added,
                        AttOriginId = imageFilename,
                        FilePath = jiraAttachment.LocalPath,
                        Comment = "Imported from Jira"
                    };
                    revision.Attachments.Add(attachment);
                    return jiraAttachment.Url;
                }
                else
                {
                    Logger.Log(LogLevel.Error, $"Inline image '{imageFilename}' in revision {revision.Index} is not an attachment.");
                    return null;
                }
            }
        }

        internal Dictionary<string, FieldMapping<JiraRevision>> InitializeFieldMappings(ExportIssuesSummary exportIssuesSummary)
        {
            Logger.Log(LogLevel.Info, "Initializing Jira field mapping...");

            var commonFields = new FieldMapping<JiraRevision>();
            var typeFields = new Dictionary<string, FieldMapping<JiraRevision>>();

            foreach (var targetType in _targetTypes)
            {
                if (!typeFields.ContainsKey(targetType))
                    typeFields.Add(targetType, new FieldMapping<JiraRevision>());
            }

            foreach (var item in _config.FieldMap.Fields)
            {
                if (item.Source != null)
                {
                    var isCustomField = item.SourceType == "name";
                    if (isCustomField && _jiraProvider.GetCustomId(item.Source) == null)
                        Logger.Log(LogLevel.Warning, $"Could not find the field id for '{item.Source}', please check the field mapping!");

                    Func<JiraRevision, (bool, object)> value;

                    if (item.Mapping?.Values != null)
                    {
                        value = r => FieldMapperUtils.MapValue(r, item.Source, item.Target, _config, _jiraProvider.GetCustomId(item.Source), exportIssuesSummary);
                    }
                    else if (!string.IsNullOrWhiteSpace(item.Mapper))
                    {
                        switch (item.Mapper)
                        {
                            case "MapTitle":
                                value = r => FieldMapperUtils.MapTitle(r);
                                break;
                            case "MapTitleWithoutKey":
                                value = r => FieldMapperUtils.MapTitleWithoutKey(r);
                                break;
                            case "MapUser":
                                value = IfChanged<string>(item.Source, isCustomField, MapUser);
                                break;
                            case "MapSprint":
                                value = IfChanged<string>(item.Source, isCustomField, FieldMapperUtils.MapSprint);
                                break;
                            case "MapSprintExtended":
                                value = r => FieldMapperUtils.MapSprintExtended(r, item.Source, isCustomField, _jiraProvider.GetCustomId(item.Source), _config);
                                break;
                            case "MapTags":
                                value = IfChanged<string>(item.Source, isCustomField, FieldMapperUtils.MapTags);
                                break;
                            case "MapArray":
                                value = IfChanged<string>(item.Source, isCustomField, FieldMapperUtils.MapArray);
                                break;
                            case "MapRemainingWork":
                                value = IfChanged<string>(item.Source, isCustomField, FieldMapperUtils.MapRemainingWork);
                                break;
                            case "MapRendered":
                                value = r => FieldMapperUtils.MapRenderedValue(r, item.Source, isCustomField, _jiraProvider.GetCustomId(item.Source), _config);
                                break;
                            case "MapLexoRank":
                                value = IfChanged<string>(item.Source, isCustomField, FieldMapperUtils.MapLexoRank);
                                break;
                            case "MapArea":
                                value = r => FieldMapperUtils.MapArea(r, item.Source, isCustomField, _jiraProvider.GetCustomId(item.Source), _config);
                                break;
                            default:
                                value = IfChanged<string>(item.Source, isCustomField);
                                break;
                        }
                    }
                    else
                    {
                        var dataType = item.Type.ToLower();
                        if (dataType == "double")
                        {
                            value = IfChanged<double>(item.Source, isCustomField);
                        }
                        else if (dataType == "int" || dataType == "integer")
                        {
                            value = IfChanged<int>(item.Source, isCustomField);
                        }
                        else if (dataType == "datetime" || dataType == "date")
                        {
                            value = IfChangedDateTime(item.Source, isCustomField);
                        }
                        else
                        {
                            value = IfChanged<string>(item.Source, isCustomField);
                        }
                    }

                    // Check if not-for has been set, if so get all work item types except that one, else for has been set and get those                 
                    var currentWorkItemTypes = !string.IsNullOrWhiteSpace(item.NotFor) ? GetWorkItemTypes(item.NotFor.Split(',')) : item.For.Split(',').ToList();

                    foreach (var wit in currentWorkItemTypes)
                    {
                        try
                        {
                            if (wit == "All" || wit == "Common")
                            {
                                commonFields.Add(item.Target, value);
                            }
                            else
                            {
                                // If we haven't mapped the Type then we probably want to ignore the field
                                if (typeFields.TryGetValue(wit, out FieldMapping<JiraRevision> fm))
                                {
                                    fm.Add(item.Target, value);
                                }
                                else
                                {
                                    Logger.Log(LogLevel.Warning, $"No target type '{wit}' is set, field {item.Source} cannot be mapped.");
                                }
                            }
                        }

                        catch (Exception)
                        {
                            Logger.Log(LogLevel.Warning, $"Ignoring target mapping with key: '{item.Target}', because it is already configured.");
                        }
                    }
                }
            }

            // Now go through the list of built up type fields (which we will eventually get to
            // and then add them to the complete dictionary per type.
            var mappingPerWiType = new Dictionary<string, FieldMapping<JiraRevision>>();
            foreach (KeyValuePair<string, FieldMapping<JiraRevision>> item in typeFields)
            {
                mappingPerWiType.Add(item.Key, MergeMapping(commonFields, item.Value));
            }

            return mappingPerWiType;
        }

        internal WiDevelopmentLink MapDevelopmentLink(JiraRevision jiraRevision)
        {
            if (jiraRevision == null)
                throw new ArgumentNullException(nameof(jiraRevision));

            if (jiraRevision.DevelopmentLink == null)
            {
                return null;
            }

            var jiraDevelopmentLink = jiraRevision.DevelopmentLink.Value;
            var respositoryTarget = jiraDevelopmentLink.Repository;

            var respositoryOverride = _config
                .RepositoryMap
                .Repositories?
                .Find(r => r.Source == respositoryTarget)?
                .Target;

            if (!string.IsNullOrEmpty(respositoryOverride))
            {
                respositoryTarget = respositoryOverride;
            }

            var developmentLink = new WiDevelopmentLink()
            {
                Id = jiraDevelopmentLink.Id,
                Repository = respositoryTarget,
                Type = jiraDevelopmentLink.Type.ToString()
            };

            return developmentLink;
        }

        internal List<WiLink> MapLinks(JiraRevision r)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            var links = new List<WiLink>();
            if (r.LinkActions == null)
                return links;

            // map issue links
            foreach (var jiraLinkAction in r.LinkActions)
            {
                if (_config.ForceIssueKeyMatch && EnsureSameIssueKey(jiraLinkAction)) continue;

                var changeType = jiraLinkAction.ChangeType == RevisionChangeType.Added ? ReferenceChangeType.Added : ReferenceChangeType.Removed;

                var link = new WiLink();

                if (_config.LinkMap.Links == null) continue;

                var linkType = (from t in _config.LinkMap.Links where t.Source == jiraLinkAction.Value.LinkType select t.Target).FirstOrDefault();

                if (linkType == null) continue;

                // If link is inward link, reverse the direction of the mapped link
                if (jiraLinkAction.Value.IsInwardLink)
                {
                    linkType = GetReverseLinkTypeReferenceName(linkType);
                }
                link.Change = changeType;
                link.SourceOriginId = jiraLinkAction.Value.SourceItem;
                link.TargetOriginId = jiraLinkAction.Value.TargetItem;
                link.WiType = linkType;

                links.Add(link);
            }

            // map epic link
            LinkMapperUtils.AddRemoveSingleLink(r, links, _jiraProvider.GetSettings().EpicLinkField, "Epic", _config);

            // map parent
            LinkMapperUtils.AddRemoveSingleLink(r, links, "parent", "Parent", _config);

            // map epic child
            LinkMapperUtils.MapEpicChildLink(r, links, "epic child", "Child", _config);


            return links;

            bool EnsureSameIssueKey(RevisionAction<JiraLink> jiraLinkAction)
            {
                // Issue keys must match
                var sourceIssueKey = jiraLinkAction.Value.SourceItem.Split('-')[0];
                var targetIssueKey = jiraLinkAction.Value.TargetItem.Split('-')[0];
                if (sourceIssueKey != targetIssueKey)
                {
                    Logger.Log(LogLevel.Warning,
                        $"{jiraLinkAction.Value.SourceItem} -> {jiraLinkAction.Value.TargetItem}: Issue keys mismatch {sourceIssueKey}!={targetIssueKey}");
                    return true;
                }

                return false;
            }
        }

        internal List<WiAttachment> MapAttachments(JiraRevision rev)
        {
            if (rev == null)
                throw new ArgumentNullException(nameof(rev));

            var attachments = new List<WiAttachment>();
            if (rev.AttachmentActions == null)
                return attachments;

            _jiraProvider.DownloadAttachments(rev).Wait();

            foreach (var att in rev.AttachmentActions)
            {
                var change = att.ChangeType == RevisionChangeType.Added ? ReferenceChangeType.Added : ReferenceChangeType.Removed;

                var wiAtt = new WiAttachment()
                {
                    Change = change,
                    AttOriginId = att.Value.Id,
                    FilePath = att.Value.LocalPath,
                    Comment = "Imported from Jira"
                };
                attachments.Add(wiAtt);
            }

            return attachments;
        }

        internal List<WiField> MapFields(JiraRevision r)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            var fields = new List<WiField>();

            if (_config.TypeMap.Types != null)
            {
                var type = (from t in _config.TypeMap.Types where t.Source == r.Type select t.Target).FirstOrDefault();

                if (type != null && _fieldMappingsPerType.TryGetValue(type, out var mapping))
                {
                    foreach (var field in mapping)
                    {
                        try
                        {
                            var fieldreference = field.Key;
                            var (include, value) = field.Value(r);

                            if (include)
                            {
                                value = TruncateField(value, fieldreference);
                                if (value == null)
                                {
                                    value = "";
                                }
                                Logger.Log(LogLevel.Debug, $"Mapped value '{value}' to field '{fieldreference}'.");
                                fields.Add(new WiField()
                                {
                                    ReferenceName = fieldreference,
                                    Value = value
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log(ex, $"Error mapping field '{field.Key}' on item '{r.OriginId}'.");
                        }
                    }
                }
            }

            return fields;
        }

        internal WiRevision MapRevision(JiraRevision r)
        {
            Logger.Log(LogLevel.Debug, $"Mapping revision {r.Index}.");

            List<WiAttachment> attachments = MapAttachments(r);
            List<WiField> fields = MapFields(r);
            List<WiLink> links = MapLinks(r);
            var developmentLink = MapDevelopmentLink(r);

            return new WiRevision()
            {
                ParentOriginId = r.ParentItem.Key,
                Index = r.Index,
                Time = r.Time,
                Author = MapUser(r.Author),
                OriginalCommentId = r.OriginalCommentId,
                Attachments = attachments,
                Fields = fields,
                Links = links,
                AttachmentReferences = attachments.Any(),
                DevelopmentLink = developmentLink
            };
        }

        protected override string MapUser(string sourceUser)
        {
            if (string.IsNullOrWhiteSpace(sourceUser))
                return null;

            var email = _jiraProvider.GetUserEmail(sourceUser);
            return base.MapUser(email);
        }

        private HashSet<string> InitializeTypeMappings()
        {
            HashSet<string> types = new HashSet<string>();
            _config.TypeMap.Types.ForEach(t => types.Add(t.Target));
            return types;
        }

        private Func<JiraRevision, (bool, object)> IfChanged<T>(string sourceField, bool isCustomField, Func<T, object> mapperFunc = null)
        {
            if (isCustomField)
            {
                sourceField = _jiraProvider.GetCustomId(sourceField) ?? sourceField;
            }

            return (r) =>
            {
                if (r.Fields.TryGetValue(sourceField.ToLower(), out object value))
                {
                    if (mapperFunc != null)
                    {
                        return (true, mapperFunc((T)value));
                    }
                    else
                    {
                        return (true, (T)value);
                    }
                }
                else
                {
                    return (false, null);
                }
            };
        }

        private Func<JiraRevision, (bool, object)> IfChangedDateTime(string sourceField, bool isCustomField, Func<DateTime, object> mapperFunc = null)
        {
            if (isCustomField)
            {
                sourceField = _jiraProvider.GetCustomId(sourceField) ?? sourceField;
            }

            return (r) =>
            {
                if (r.Fields.TryGetValue(sourceField.ToLower(), out object value))
                {
                    if (mapperFunc != null)
                    {
                        return (true, mapperFunc((DateTime)value));
                    }
                    else
                    {
                        if (DateTime.TryParseExact(value.ToString(), "dd/MMM/yy", CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out DateTime result))
                        {
                            return (true, result.ToUniversalTime());
                        }
                        else
                        {
                            return (true, value);
                        }
                    }
                }
                else
                {
                    return (false, null);
                }
            };
        }

        #endregion

        private List<string> GetWorkItemTypes(params string[] notFor)
        {
            List<string> list;
            if (notFor != null && notFor.Any())
            {
                list = _targetTypes.Where(t => !notFor.Contains(t)).ToList();
            }
            else
            {
                list = _targetTypes.ToList();
            }
            return list;
        }

        internal object TruncateField(object value, string field)
        {
            if (value == null) return value;
            string valueStr = value.ToString();
            var fieldLimits = new Dictionary<string, int>()
            {
                { WiFieldReference.Title, 255 },
                { WiFieldReference.Description, 1048576 }
            };
            if (fieldLimits.ContainsKey(field))
            {
                int limit = fieldLimits[field];
                if (valueStr.Length > limit)
                {
                    string truncated = valueStr.Substring(0, limit - 3) + "...";
                    Logger.Log(LogLevel.Warning, $"Field {field} was truncated. Maximum length: {limit}, new value: {truncated}");
                    return truncated;
                }
            }
            return valueStr;
        }

        private string GetReverseLinkTypeReferenceName(string referenceName)
        {
            string Forward = "Forward";
            string Reverse = "Reverse";
            if (referenceName.Contains(Forward))
            {
                return referenceName.Replace(Forward, Reverse);
            }
            else
            {
                return referenceName.Replace(Reverse, Forward);
            }
        }
    }
}
