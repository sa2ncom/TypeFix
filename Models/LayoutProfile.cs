namespace TypeFix.Models
{
    public sealed class LayoutProfile
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required LayoutConfig Config { get; init; }
        public bool IsBuiltIn { get; init; }
        public string? FilePath { get; init; }
    }
}
