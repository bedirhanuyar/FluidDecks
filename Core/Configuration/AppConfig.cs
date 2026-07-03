using System;
using System.Collections.Generic;

namespace FluidDecks.Core.Configuration
{
    public enum FolderMode
    {
        MirrorDesktop,
        CategoryBased,
        VirtualDecks
    }

    public enum BlurMode
    {
        None,
        Standard,
        Acrylic,
        Transparent
    }

    public class AppConfig
    {
        public bool IsFirstRun { get; set; } = true;

        public int MaxDepth { get; set; } = 1;
        
        public FolderMode FolderLayoutMode { get; set; } = FolderMode.VirtualDecks;
        
        public bool AutoGridColumnCount { get; set; } = true;
        public int GridColumnCount { get; set; } = 4;
        
        /// <summary>
        /// Global icon scale and sizing parameters.
        /// </summary>
        public double IconSize { get; set; } = 45.0;
        public double IconScale { get; set; } = 1.0;
        
        /// <summary>
        /// Configures the layout constraints for the virtual folder popups.
        /// </summary>
        public double FolderPanelScale { get; set; } = 1.0;
        public double PopupMaxScreenRatio { get; set; } = 0.6; // Limits popup size to a percentage of total screen area

        public bool IsPaused { get; set; } = false;

        /// <summary>
        /// Defines the physics engine and animation settings for fluid interactions.
        /// </summary>
        public bool EnablePhysics { get; set; } = false;
        public double AnimationSpeed { get; set; } = 1.0;
        public int AnimationEasing { get; set; } = 0; // 0: Quartic, 1: Cubic, 2: Back/Bouncy

        /// <summary>
        /// Visual appearance settings including blur types and theme colors.
        /// </summary>
        public bool EnableBlurEffect { get; set; } = true;
        public BlurMode BackgroundBlurMode { get; set; } = BlurMode.Standard;
        public double BackgroundOpacity { get; set; } = 0.5; // Determines the visibility of the underlying desktop wallpaper
        public double BlurTintOpacity { get; set; } = 0.12; // 0.0 = fully transparent, 1.0 = opaque solid color
        public string BlurTintColor { get; set; } = "#000000"; // Hex color applied as a tint over the blur

        /// <summary>
        /// UI rounding parameters for different states of the virtual folders.
        /// </summary>
        public double CollapsedCornerRadius { get; set; } = 8.0;
        public double ExpandedCornerRadius { get; set; } = 16.0;

        public int BlurCornerPreference { get; set; } = 2; // 0 = Rectangle, 1 = RoundSmall, 2 = Round (DWM overrides)
        
        /// <summary>
        /// Diagnostic and developer options.
        /// </summary>
        public bool DeveloperModeLogging { get; set; } = true;

        /// <summary>
        /// Settings that necessitate an application restart to take effect.
        /// </summary>
        public bool EnableHardwareAcceleration { get; set; } = true;
        
        public bool EnableExperimentalFeatures { get; set; } = false;
        
        public bool OpenVirtualFoldersInApp { get; set; } = false;

        /// <summary>
        /// Maps file extensions to internal categorical deck names.
        /// </summary>
        public Dictionary<string, string> ExtensionToCategoryMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".lnk", "Shortcuts" },
            { ".url", "Shortcuts" },
            { ".exe", "Programs" },
            { ".docx", "Documents" },
            { ".pdf", "Documents" },
            { ".txt", "Documents" },
            { ".png", "Images" },
            { ".jpg", "Images" },
            { ".jpeg", "Images" },
            { ".zip", "Archives" },
            { ".rar", "Archives" }
        };

        /// <summary>
        /// State collection storing the customized positions and sizes of all desktop panels.
        /// </summary>
        public List<PanelConfig> Panels { get; set; } = new List<PanelConfig>
        {
            new PanelConfig { CategoryName = "Shortcuts", X = 50, Y = 50 },
            new PanelConfig { CategoryName = "Documents", X = 350, Y = 50 },
            new PanelConfig { CategoryName = "Images", X = 650, Y = 50 }
        };
    }

    public class PanelConfig
    {
        public string CategoryName { get; set; }
        public double X { get; set; } = 100;
        public double Y { get; set; } = 100;
        public double Width { get; set; } = 250;
        public bool IsPositionLocked { get; set; } = false;
        public FolderMode ModeOwner { get; set; } = FolderMode.MirrorDesktop;
        public List<string> VirtualItems { get; set; } = new List<string>();
        public System.Collections.Generic.List<string> VirtualFolders { get; set; } = new System.Collections.Generic.List<string>();
        public Dictionary<string, string> VirtualItemNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
