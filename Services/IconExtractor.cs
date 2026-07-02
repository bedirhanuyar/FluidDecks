using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using FluidDecks.Core.Win32;

namespace FluidDecks.Services
{
    public static class IconExtractor
    {
        public static BitmapSource GetIcon(string filePath, out IntPtr hIcon)
        {
            hIcon = IntPtr.Zero;
            Shell32.SHFILEINFO shinfo = new Shell32.SHFILEINFO();

            // Get the large icon for the file
            IntPtr ptr = Shell32.SHGetFileInfo(
                filePath, 
                0, 
                out shinfo, 
                (uint)Marshal.SizeOf(shinfo), 
                Shell32.SHGFI_ICON | Shell32.SHGFI_LARGEICON);

            if (ptr != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                hIcon = shinfo.hIcon;
                try
                {
                    BitmapSource img = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    
                    // Freeze the bitmap to allow it to be shared across threads safely and avoid memory leaks
                    img.Freeze();
                    return img;
                }
                catch
                {
                    // Fallback or handle safely
                }
            }
            return null;
        }

        public static void ReleaseIcon(IntPtr hIcon)
        {
            if (hIcon != IntPtr.Zero)
            {
                User32.DestroyIcon(hIcon);
            }
        }
    }
}
