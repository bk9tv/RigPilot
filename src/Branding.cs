using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RigPilot {
    public static class Branding {
        static readonly BitmapSource logo = Load("RigPilot.Logo.png");
        static readonly BitmapSource icon = Load("RigPilot.Icon.ico");

        static BitmapSource Load(string name) {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
                if (stream == null) throw new InvalidOperationException("App-Symbol fehlt: " + name);
                var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                image.Freeze(); return image;
            }
        }
        public static ImageSource WindowIcon { get { return icon; } }
        public static Image Mark(double size) {
            var image = new Image { Source = logo, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
    }
}
