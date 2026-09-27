namespace TypeFix.Models
{
    public class LayoutPair
    {
        public string Name { get; set; } = "Unnamed";
        public string Id { get; set; } = "pair";

        public string Layout1Name { get; set; } = "Layout1";
        public string Layout2Name { get; set; } = "Layout2";

        public string Layout1Chars { get; set; } = "";
        public string Layout2Chars { get; set; } = "";

        /// <summary>Shift-layer characters for Layout1 (same length/order as Layout1Chars).</summary>
        public string Layout1ShiftChars { get; set; } = "";

        /// <summary>Shift-layer characters for Layout2 (same length/order as Layout2Chars).</summary>
        public string Layout2ShiftChars { get; set; } = "";
    }
}
