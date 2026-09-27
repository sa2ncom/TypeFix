using System.Collections.Generic;

namespace TypeFix.Models
{
    public class LayoutConfig
    {
        /// <summary>Stable id for this file. One file is one selectable layout.</summary>
        public string? Id { get; set; }

        /// <summary>Label shown in the app, for example "Persian ↔ English".</summary>
        public string? Name { get; set; }

        public List<LayoutPair> LayoutPairs { get; set; } = new();
    }
}
