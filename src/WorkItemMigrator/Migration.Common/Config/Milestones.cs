using System.Collections.Generic;

namespace Migration.Common.Config
{
    public class Milestones
    {
        public string Default { get; set; }
        public List<Milestone> Milestone { get; set; }
    }

    public class Milestone
    {
        public int Threshold { get; set; }
        public string Name { get; set; }
    }
}