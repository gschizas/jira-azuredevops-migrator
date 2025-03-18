using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JiraExport;

public class JiraSprint
{
    public int Id { get; set; }
    public int RapidViewId { get; set; }
    public string State { get; set; }
    public string Name { get; set; }

    public int SprintNumber
    {
        get
        {
            var regex = new Regex(@"\d+");
            var matches = regex.Matches(Name);
            var maxNumber = 0;

            foreach (Match match in matches)
            {
                var number = int.Parse(match.Value);
                if (number > maxNumber)
                {
                    maxNumber = number;
                }
            }

            return maxNumber;
        }
    }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? CompleteDate { get; set; }
    public DateTime? ActivatedDate { get; set; }
    public int Sequence { get; set; }
    public string Goal { get; set; }
    public bool Synced { get; set; }
    public bool AutoStartStop { get; set; }
    public string IncompleteIssuesDestinationId { get; set; }

    private static DateTime? ParseDateTime(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        if (value == "<null>")
            return null;

        return DateTime.Parse(value, null, DateTimeStyles.RoundtripKind);
    }
    public static List<JiraSprint> ParseList(string input)
    {
        const string pattern = @"(\w+)=((?:.|\n)*?)(?=(?:,\w+=)|(?:]))";

        var sprints = new List<JiraSprint>();

        var sprintStrings = input.Split(new[] { "];" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var sprintString in sprintStrings)
        {
            var sprint = new JiraSprint();

            // Regular expression pattern to match the properties

            // Find all matches using the regex pattern
            var matches = Regex.Matches(sprintString, pattern, RegexOptions.Singleline);

            // Extract and print the properties
            foreach (Match match in matches)
            {
                var key = match.Groups[1].Value;
                var value = match.Groups[2].Value;

                switch (key)
                {
                    case "id":
                        sprint.Id = int.Parse(value);
                        break;
                    case "rapidViewId":
                        sprint.RapidViewId = int.Parse(value);
                        break;
                    case "state":
                        sprint.State = value;
                        break;
                    case "name":
                        sprint.Name = value;
                        break;
                    case "startDate":
                        sprint.StartDate = ParseDateTime(value);
                        break;
                    case "endDate":
                        sprint.EndDate = ParseDateTime(value);
                        break;
                    case "completeDate":
                        sprint.CompleteDate = ParseDateTime(value);
                        break;
                    case "activatedDate":
                        sprint.ActivatedDate = ParseDateTime(value);
                        break;
                    case "sequence":
                        sprint.Sequence = int.Parse(value);
                        break;
                    case "goal":
                        sprint.Goal = value;
                        break;
                    case "synced":
                        sprint.Synced = bool.Parse(value);
                        break;
                    case "autoStartStop":
                        sprint.AutoStartStop = bool.Parse(value);
                        break;
                    case "incompleteIssuesDestinationId":
                        sprint.IncompleteIssuesDestinationId = value;
                        break;
                    default:
                        throw new Exception($"Unknown key: {key}");
                }
            }
            sprints.Add(sprint);
        }

        return sprints;
    }
}
