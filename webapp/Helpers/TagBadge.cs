using System;

namespace bwkt_webapp.Helpers
{
    /// <summary>
    /// Provides mapping from a video tag code to a display text and Bootstrap badge CSS class.
    /// </summary>
    public static class TagBadge
    {
        /// <summary>
        /// Returns (Text, CssClass) for the given tag code.
        /// </summary>
        public static (string Text, string CssClass) Get(string code) =>
            code?.ToLowerInvariant() switch
            {
                // Race the video is for.
                "z" => ("Zerg",    "bg-danger"),
                "p" => ("Protoss", "bg-warning"),
                "t" => ("Terran",  "bg-primary"),
                // Matchup.
                "zvz" => ("ZvP", "bg-danger"),
                "zvt" => ("ZvT", "bg-danger"),
                "zvp" => ("ZvP", "bg-danger"),
                "pvz" => ("PvZ", "bg-warning"),
                "pvt" => ("PvT", "bg-warning"),
                "pvp" => ("PvP", "bg-warning"),
                "tvz" => ("TvZ", "bg-primary"),
                "tvt" => ("TvT", "bg-primary"),
                "tvp" => ("TvP", "bg-primary"),
                // Misc.
                _   => (code ?? string.Empty, "bg-secondary"),
            };
    }
}