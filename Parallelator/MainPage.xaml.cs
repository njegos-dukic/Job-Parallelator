using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel.Core;
using Windows.Media.Capture;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;

using Microsoft.Toolkit.Uwp.Notifications;


namespace Parallelator
{
    public sealed partial class MainPage : Page
    {
        private static List<StorageFile> files = new List<StorageFile>();
        private static List<ImageEdit> tasked = new List<ImageEdit>();
        private static int parallelLimit = 0;
        public static int taskedCounter = 0;
        public static SemaphoreSlim contentDialogSemaphore = new SemaphoreSlim(1);
        public static SemaphoreSlim counterSemaphore = new SemaphoreSlim(1);
        public static SemaphoreSlim suspendingSemaphore = new SemaphoreSlim(1);

        public delegate void StackPanelUpdatedDelegate(ImageEdit ie);
        public static event StackPanelUpdatedDelegate PanelUpdated;

        public delegate void FlipViewUpdatedDelegate(List<StorageFile> files);
        public static event FlipViewUpdatedDelegate FlipViewUpdated;

        public delegate void LimiUpdatedDelegate();
        public static event LimiUpdatedDelegate LimitUpdated;

        public MainPage()
        {
            this.InitializeComponent();
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = false;
            ApplicationView.PreferredLaunchViewSize = new Windows.Foundation.Size(1280, 720);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;
            AddHandlers();
        }

        private void AddHandlers()
        {
            selectImageButton.Click += SelectImages;
            parallelizationLimit.KeyDown += LimitPressedEnter;
            load.Click += Load;
            startAll.Click += StartAll;
            takePhoto.Click += TakePhoto;
            PanelUpdated += UpdateStackPanel;
            FlipViewUpdated += AddFilesToFlipView;
            LimitUpdated += UpdateParallelLimit;
        }

        private async void SelectImages(object sender, RoutedEventArgs e)
        {
            // await BackgroundNotification.SendNotification();
            await contentDialogSemaphore.WaitAsync();
            var chosenFiles = await AsyncSelectFilesFromDialog();
            contentDialogSemaphore.Release();

            if (chosenFiles.Count == 0)
                return;

            foreach (var f in chosenFiles)
                files.Add(f);

            AddFilesToFlipView(chosenFiles.ToList());
        }

        private async void LimitPressedEnter(object sender, KeyRoutedEventArgs e)
        {
            if (Convert.ToInt32(e.Key) != 13)
                return;

            await ReadLimitFromTextBox();
        }

        private async void Load(object sender, RoutedEventArgs e)
        {
            if (!await ReadLimitFromTextBox())
                return;

            if (files.Count == 0)
            {
                ContentDialog invalidInput = new ContentDialog()
                {
                    Title = "No input",
                    Content = "Please select files from disk or capture from Camera.",
                    CloseButtonText = "OK"
                };
                await contentDialogSemaphore.WaitAsync();
                await invalidInput.ShowAsync();
                contentDialogSemaphore.Release();
                return;
            }

            for ( ; taskedCounter < parallelLimit && files.Count != 0; )
            {
                ImageEdit ie = new ImageEdit(files.First());
                files.RemoveAt(0);
                tasked.Add(ie);
                await MainPage.counterSemaphore.WaitAsync();
                Interlocked.Increment(ref MainPage.taskedCounter);
                MainPage.counterSemaphore.Release();
                sp.Children.Insert(0, ie);
            }
        }

        private async void StartAll(object sender, RoutedEventArgs e)
        {
            if (parallelLimit <= 0)
            {
                ContentDialog invalidInput = new ContentDialog()
                {
                    Title = "Invalid input",
                    Content = "Please enter positive integer value as parallelization limit.",
                    CloseButtonText = "OK"
                };

                await contentDialogSemaphore.WaitAsync();
                await invalidInput.ShowAsync();
                contentDialogSemaphore.Release();

                return;
            }

            bool allCompleted = true;

            foreach (var t in tasked)
                if (!t.GetGrayscale().IsCompleted)
                    allCompleted = false;

            if (allCompleted)
            {
                ContentDialog invalidInput = new ContentDialog()
                {
                    Title = "No input",
                    Content = "Please load files for editing.",
                    CloseButtonText = "OK"
                };

                await contentDialogSemaphore.WaitAsync();
                await invalidInput.ShowAsync();
                contentDialogSemaphore.Release();

                return;
            }

            foreach (var t in tasked)
                if (t.GetParallelism() <= 0)
                {
                    ContentDialog invalidInput = new ContentDialog()
                    {
                        Title = "Cores not set",
                        Content = "Please specify number of cores for each task.",
                        CloseButtonText = "OK"
                    };

                    await contentDialogSemaphore.WaitAsync();
                    await invalidInput.ShowAsync();
                    contentDialogSemaphore.Release();

                    return;
                }

            foreach (var t in tasked)
            {
                if (!t.GetGrayscale().IsCompleted && t.GetState() != ImageEdit.EditState.Running)
                    t.Start();
            }
        }

        private async void TakePhoto(object sender, RoutedEventArgs e)
        {
            CameraCaptureUI captureUI = new CameraCaptureUI();
            captureUI.PhotoSettings.Format = CameraCaptureUIPhotoFormat.Jpeg;

            await contentDialogSemaphore.WaitAsync();
            StorageFile photo = await captureUI.CaptureFileAsync(CameraCaptureUIMode.Photo);
            contentDialogSemaphore.Release();

            if (photo == null)
                return;

            files.Add(photo);
            fv.Items.Add(new Image { Source = await FromStorageFile(photo) });
        }

        private async Task<bool> ReadLimitFromTextBox()
        {
            int value;

            try
            {
                value = Int32.Parse(parallelizationLimit.Text);
                if (value <= 0)
                    throw new Exception();
            }

            catch
            {
                ContentDialog invalidInput = new ContentDialog()
                {
                    Title = "Invalid input",
                    Content = "Please enter positive integer value as parallelization limit.",
                    CloseButtonText = "OK"
                };
                parallelizationLimit.Text = "";
                await contentDialogSemaphore.WaitAsync();
                await invalidInput.ShowAsync();
                contentDialogSemaphore.Release();
                return false;
            }

            parallelLimit = value;
            parallelizationLimit.IsEnabled = false;
            return true;
        }

        private async Task<IReadOnlyList<StorageFile>> AsyncSelectFilesFromDialog()
        {

            FileOpenPicker openPicker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.Desktop
            };

            openPicker.FileTypeFilter.Add(".bmp");
            openPicker.FileTypeFilter.Add(".jpg");
            openPicker.FileTypeFilter.Add(".png");
            openPicker.FileTypeFilter.Add(".jpeg");

            return await openPicker.PickMultipleFilesAsync();
        }

        private async void AddFilesToFlipView(List<StorageFile> files)
        {
            foreach (var f in files)
            {
                var im = new Image { Source = await FromStorageFile(f) };
                fv.Items.Add(im);
            }
        }

        private static async Task<BitmapImage> FromStorageFile(StorageFile sf)
        {
            using (var randomAccessStream = await sf.OpenAsync(FileAccessMode.Read))
            {
                var result = new BitmapImage();
                await result.SetSourceAsync(randomAccessStream);
                return result;
            }
        }

        public static int GetParallelizationLimit()
        {
            return parallelLimit;
        }

        public static List<StorageFile> GetQueuedFiles()
        {
            return files;
        }

        public static List<ImageEdit> GetTaskedFiles()
        {
            return tasked;
        }

        public static ImageEdit GetNext()
        {
            if (files.Count == 0)
                return null;

            ImageEdit ie = new ImageEdit(files.First());
            files.RemoveAt(0);

            tasked.Add(ie);
            PanelUpdated.Invoke(ie);
            return ie;
        }

        private void UpdateStackPanel(ImageEdit ie)
        {
            sp.Children.Insert(0, ie);
        }

        public static async Task SaveState()
        {
            try
            {
                foreach (var f in await ApplicationData.Current.LocalFolder.GetFilesAsync())
                    await f.DeleteAsync();

                await SaveTasked();
                await SaveQueued();
            }
            catch
            {
                return;
            }
            finally { }
        }

        private static async Task SaveTasked()
        {
            if (tasked.Count <= 0)
                return;

            var taskedXElements = new XElement("Tasked");

            foreach (var t in tasked)
            {
                if (t == null || t.GetStorageFile() == null)
                    continue;

                var tFile = t.GetStorageFile();

                if (t.GetState() != ImageEdit.EditState.Saved && t.GetState() != ImageEdit.EditState.Finished)
                {
                    var nFile = await tFile.CopyAsync(ApplicationData.Current.LocalFolder);
                    t.SetStorageFile(nFile);
                    taskedXElements.Add(t.ToXElement());
                }

            }

            var taskedFile = await ApplicationData.Current.LocalFolder.CreateFileAsync("Tasked.xml", CreationCollisionOption.ReplaceExisting);
            taskedXElements.Save(taskedFile.Path);
        }

        private static async Task SaveQueued()
        {
            if (parallelLimit <= 0)
                return;

            var loaded = MainPage.GetQueuedFiles();
            if (loaded.Count <= 0)
                return;

            var loadedXElements = new XElement("Loaded");
            loadedXElements.Add(new XElement("Limit", parallelLimit.ToString()));

            foreach (var f in files)
            {
                var copied = await f.CopyAsync(ApplicationData.Current.LocalFolder);
                loadedXElements.Add(new XElement("File", copied.Path));
            }
            var filesFile = await ApplicationData.Current.LocalFolder.CreateFileAsync("Files.xml", CreationCollisionOption.ReplaceExisting);
            loadedXElements.Save(filesFile.Path);
        }

        public async static void ResumeState()
        {
            try
            {
                await LoadStackPanel();
                await LoadQueuedFiles();
            }
            catch
            {
                return;
            }
        }

        private static async Task LoadStackPanel()
        {
            var pathToTasked = await ApplicationData.Current.LocalFolder.GetFileAsync("Tasked.xml");
            var taskedFromXML = XElement.Load(pathToTasked.Path).Elements("ImageEdit");

            foreach (var job in taskedFromXML)
                if (!job.Element("State").Value.Equals("Finished") && !job.Element("State").Value.Equals("Saved"))
                {
                    var task = new ImageEdit(await StorageFile.GetFileFromPathAsync(job.Element("StorageFile").Value));
                    task.SetParallelLimit(Int32.Parse(job.Element("ParallelLimit").Value));
                    tasked.Add(task);
                    PanelUpdated(task);
                    await MainPage.counterSemaphore.WaitAsync();
                    Interlocked.Increment(ref MainPage.taskedCounter);
                    MainPage.counterSemaphore.Release();
                    FlipViewUpdated.Invoke(new List<StorageFile>() { task.GetStorageFile() });
                }
        }

        private static async Task LoadQueuedFiles()
        {
            var pathToQueued = await ApplicationData.Current.LocalFolder.GetFileAsync("Files.xml");
            var queuedFromXML = XElement.Load(pathToQueued.Path).Elements("File");

            parallelLimit = Int32.Parse(XElement.Load(pathToQueued.Path).Elements("Limit").First().Value);
            LimitUpdated.Invoke();

            foreach (var t in queuedFromXML)
            {
                var sf = await StorageFile.GetFileFromPathAsync(t.Value);
                files.Add(sf);
            }

            FlipViewUpdated.Invoke(files);

        }

        private void UpdateParallelLimit()
        {
            parallelizationLimit.Text = parallelLimit.ToString();
            parallelizationLimit.IsEnabled = false;
        }

        private void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {

        }
    }
}
