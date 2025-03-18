using JiraExport;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Jira_Export.Tests
{
    [TestFixture]
    internal class JiraSprint
    {
        private string _input;
        [SetUp]
        public void Setup()
        {
            _input = "com.atlassian.greenhopper.service.sprint.Sprint@4dfdeaa6[id=3589,rapidViewId=137,state=CLOSED,name=Anonymous Sprint 199,startDate=2024-11-25T06:52:00.000+02:00,endDate=2024-12-06T18:56:00.000+02:00,completeDate=2024-12-09T13:17:10.358+02:00,activatedDate=2024-11-25T11:31:34.900+02:00,sequence=3589,goal=,synced=false,autoStartStop=false,incompleteIssuesDestinationId=<null>];com.atlassian.greenhopper.service.sprint.Sprint@5845ae14[id=3590,rapidViewId=137,state=CLOSED,name=Anonymous Sprint 200,startDate=2024-12-09T06:56:00.000+02:00,endDate=2024-12-20T18:56:00.000+02:00,completeDate=2024-12-23T11:00:14.044+02:00,activatedDate=2024-12-09T13:19:04.228+02:00,sequence=3552,goal=,synced=false,autoStartStop=false,incompleteIssuesDestinationId=<null>];com.atlassian.greenhopper.service.sprint.Sprint@1958ebd5[id=3591,rapidViewId=137,state=CLOSED,name=Anonymous Sprint 201,startDate=2024-12-23T06:56:00.000+02:00,endDate=2025-01-03T18:56:00.000+02:00,completeDate=2025-01-07T11:12:03.417+02:00,activatedDate=2024-12-23T11:18:06.061+02:00,sequence=3590,goal=Καλά Χριστούγεννα με επιτυχημένο milestone,synced=false,autoStartStop=false,incompleteIssuesDestinationId=<null>]";
        }

        [Test]
        public void ParseValue()
        {
            var sprints = JiraExport.JiraSprint.ParseList(_input);

            // write tests for this method
            Assert.AreEqual(3, sprints.Count);
            Assert.AreEqual(3589, sprints[0].Id);
            Assert.AreEqual(137, sprints[0].RapidViewId);
            Assert.AreEqual("CLOSED", sprints[0].State);
            Assert.AreEqual("Anonymous Sprint 199", sprints[0].Name);
            Assert.AreEqual(new DateTime(2024, 11, 25, 6, 52, 0), sprints[0].StartDate);
            Assert.AreEqual(new DateTime(2024, 12, 6, 18, 56, 0), sprints[0].EndDate);
            Assert.AreEqual(new DateTime(), sprints[0].CompleteDate);

            Assert.AreEqual(3590, sprints[1].Id);
            Assert.AreEqual(137, sprints[1].RapidViewId);
            Assert.AreEqual("CLOSED", sprints[1].State);
            Assert.AreEqual("Anonymous Sprint 200", sprints[1].Name);
            Assert.AreEqual(new DateTime(2024, 12, 9, 6, 56, 0), sprints[1].StartDate);
            Assert.AreEqual(new DateTime(2024, 12, 20, 18, 56, 0), sprints[1].EndDate);

            Assert.AreEqual(3591, sprints[2].Id);
            Assert.AreEqual(137, sprints[2].RapidViewId);
            Assert.AreEqual("CLOSED", sprints[2].State);
            Assert.AreEqual("Anonymous Sprint 201", sprints[2].Name);
            Assert.AreEqual(new DateTime(2024, 12, 23, 6, 56, 0), sprints[2].StartDate);
            Assert.AreEqual(new DateTime(2025, 1, 3, 18, 56, 0), sprints[2].EndDate);



            foreach (var sprint in sprints)
            {
                Console.WriteLine($"Sprint ID: {sprint.Id}");
                Console.WriteLine($"Sprint Name: {sprint.Name}");
                Console.WriteLine($"Start Date: {sprint.StartDate}");
                Console.WriteLine($"End Date: {sprint.EndDate}");
                Console.WriteLine($"State: {sprint.State}");
                Console.WriteLine();
            }
        }
    }
}
