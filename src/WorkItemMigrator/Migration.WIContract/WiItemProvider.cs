﻿using Migration.Common.Log;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Migration.WIContract
{
    public class WiItemProvider
    {
        private readonly string _itemsDir;

        public WiItemProvider(string itemsDir)
        {
            _itemsDir = itemsDir;
        }

        public WiItem Load(string originId)
        {
            var path = Path.Combine(_itemsDir, $"{originId}.json");
            return LoadFile(path);
        }

        private WiItem LoadFile(string path)
        {
            var serialized = File.ReadAllText(path);

            if (Regex.Matches(serialized, @"\\\\u[0-F]{4,}").Count > 0)
            {
                Logger.Log(LogLevel.Warning, "Detected unicode characters, removed.");
                serialized = Regex.Replace(serialized, @"\\\\u[0-F]{4,}", "");
            }

            serialized = serialized.Replace("\\u001b", "\n"); // Clean up Unicode escape characters

            var deserialized = JsonConvert.DeserializeObject<WiItem>(serialized, new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });

            foreach (var rev in deserialized.Revisions)
                rev.ParentOriginId = deserialized.OriginId;

            return deserialized;
        }

        public void Save(WiItem item)
        {
            string path = Path.Combine(_itemsDir, $"{item.OriginId}.json");
            var serialized = JsonConvert.SerializeObject(item, Formatting.Indented);
            File.WriteAllText(path, serialized);
        }

        public IEnumerable<WiItem> EnumerateAllItems()
        {
            var result = new List<WiItem>();

            foreach (var filePath in Directory.EnumerateFiles(_itemsDir, "*.json"))
            {
                try
                {
                    result.Add(LoadFile(filePath));
                }
                catch (Exception)
                {
                    Logger.Log(LogLevel.Warning, $"Failed to load '{Path.GetFileName(filePath)}', possible reasons:\n " +
                        $"- Perhaps not a migration file?\n " +
                        $"- Perhaps the file contains unhandled Unicode sequences (e.g. \\u1234, \\uXXXX) or non-unicode characters? Invalid characters must be stripped.");
                }
            }
            return result;
        }
    }
}