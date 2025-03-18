using System.Collections.Generic;

namespace Migration.Common.Config
{
    public class FieldMap
    {
        public List<Field> Fields { get; set; }
        public Dictionary<string, int> Overrides { get; set; }
    }
}