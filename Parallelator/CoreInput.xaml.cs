using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Parallelator
{
    public sealed partial class CoreInput : ContentDialog
    {
        private ImageEdit updatedImage;

        public CoreInput(ImageEdit ie)
        {
            this.InitializeComponent();
            title.Title = ie.GetName();
            updatedImage = ie;
            input.KeyDown += EnteredCoresKeyboard;
        }

        private void EnteredCores(ContentDialog sender, ContentDialogButtonClickEventArgs e)
        {
            int value;

            try
            {
                value = Int32.Parse(input.Text);
                if (value <= 0)
                    throw new Exception();
            }
            catch
            {
                input.Text = "";
                return;
            }

            SetCores(value);
        }

        private void EnteredCoresKeyboard(object sender, KeyRoutedEventArgs e)
        {
            if (Convert.ToInt32(e.Key) == 13)
            {
                EnteredCores(this, null);
                this.Hide();
            }
        }

        private void SetCores(int x)
        {
            updatedImage.SetCores(x);
        }
    }
}
