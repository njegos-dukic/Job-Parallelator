using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;

namespace Parallelator
{
    public sealed partial class ImageEdit : Page
    {
        private static StorageFolder folder = null;
        private StorageFile inputFile = null;
        private Grayscale grayscale = new Grayscale();
        private int paralleismLevel = 0;
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private EditState currentState;

        public WriteableBitmap GrayedBitmap { get; set; } = null;

        public enum EditState { Ready, Running, Paused, Cancelled, Errored, Finished, Saved }
        
        public ImageEdit(StorageFile sf)
        {
            this.InitializeComponent();
            fileName.Text = sf.Name;
            inputFile = sf;
            currentState = EditState.Ready;
            AddHandlers();
        }

        private void AddHandlers()
        {
            fileName.DoubleTapped += FilenameDoubleClick;
            coreLimit.KeyDown += CoreLimitEnterPressed;
            startButton.Click += StartClick;
            pauseButton.Click += PauseClick;
            stopButton.Click += StopClick;
            saveButton.Click += SaveClick;
            grayscale.ProgressChanged += ProgressChanged;
        }

        private async void FilenameDoubleClick(object sender, RoutedEventArgs e)
        {
            ContentDialog popup = new ContentDialog();
            popup.Title = "[Grayscaled] " + Path.GetFileNameWithoutExtension(inputFile.Path);
            popup.CloseButtonText = "OK";
            Image im = new Image
            {
                Source = GrayedBitmap
            };
            popup.Content = im;

            if (GrayedBitmap == null || currentState == EditState.Ready)
                popup.Content = "Grayscaling in progress.";

            await MainPage.contentDialogSemaphore.WaitAsync();
            await popup.ShowAsync();
            MainPage.contentDialogSemaphore.Release();
        }

        private async void CoreLimitEnterPressed(object sender, KeyRoutedEventArgs e)
        {
            if (!coreLimit.IsEnabled)
                return;

            else if (Convert.ToInt32(e.Key) != 13)
                return;

            int value = 0;

            try
            {
                value = Int32.Parse(coreLimit.Text);
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
                coreLimit.Text = "";
                await MainPage.contentDialogSemaphore.WaitAsync();

                await invalidInput.ShowAsync();
                MainPage.contentDialogSemaphore.Release();
                return;
            }

            paralleismLevel = value;
            coreLimit.IsEnabled = false;
        }

        private async void StartClick(object sender, RoutedEventArgs e)
        {
            if (paralleismLevel <= 0)
            {
                ContentDialog invalidInput = new ContentDialog()
                {
                    Title = "Cores not set",
                    Content = "Please specify number of cores for each task.",
                    CloseButtonText = "OK"
                };

                await MainPage.contentDialogSemaphore.WaitAsync();
                await invalidInput.ShowAsync();
                MainPage.contentDialogSemaphore.Release();

                return;
            }
            
            Start();
        }

        private async void PauseClick(object sender, RoutedEventArgs e)
        {
            if (currentState == EditState.Finished)
            {
                ContentDialog editStarted = new ContentDialog();
                editStarted.Title = "Edit completed";
                editStarted.Content = "Grayscaling completed. Please save file if needed.";
                editStarted.CloseButtonText = "OK";

                await MainPage.contentDialogSemaphore.WaitAsync();
                await editStarted.ShowAsync();
                MainPage.contentDialogSemaphore.Release();

                return;
            }

            else if (currentState != EditState.Running)
                return;

            await grayscale.pauseSemaphore.WaitAsync();
            currentState = EditState.Paused;
        }

        private async void StopClick(object sender, RoutedEventArgs e)
        {
            if (currentState == EditState.Finished)
            {
                ContentDialog editStarted = new ContentDialog();
                editStarted.Title = "Edit completed";
                editStarted.Content = "Grayscaling completed. Please save file if needed.";
                editStarted.CloseButtonText = "OK";

                await MainPage.contentDialogSemaphore.WaitAsync();
                await editStarted.ShowAsync();
                MainPage.contentDialogSemaphore.Release();

                return;
            }   
            
            else if (currentState == EditState.Running || currentState == EditState.Paused)
                this.cancellationTokenSource.Cancel();
        }

        private async void SaveClick(object sender, RoutedEventArgs e)
        {
            if (currentState != EditState.Finished)
            {
                ContentDialog editStarted = new ContentDialog();
                editStarted.Title = "Please wait";
                editStarted.Content = "Please wait for edit to complete before saving.";
                editStarted.CloseButtonText = "OK";

                await MainPage.contentDialogSemaphore.WaitAsync();
                await editStarted.ShowAsync();
                MainPage.contentDialogSemaphore.Release();

                return;
            }

            else if (folder == null)
            {
                FolderPicker folderPicker = new FolderPicker() { SuggestedStartLocation = PickerLocationId.Downloads };
                folderPicker.FileTypeFilter.Add("*");

                await MainPage.contentDialogSemaphore.WaitAsync();

                folder = await folderPicker.PickSingleFolderAsync();
                MainPage.contentDialogSemaphore.Release();

                if (folder != null)
                    Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.AddOrReplace("PickedFolderToken", folder);

                else
                    return;
            }

            if (GrayedBitmap == null)
                return;

            else
            {
                try
                {
                    StorageFile storageFile = await folder.CreateFileAsync("[Grayscaled] " + Path.GetFileNameWithoutExtension(inputFile.Path) + ".jpg");
                    await Parallelator.Grayscale.WriteableBitmapToStorageFile(GrayedBitmap, storageFile);
                    currentState = EditState.Saved;
                    saveButton.IsEnabled = false;
                }

                catch (Exception)
                {
                    ContentDialog editStarted = new ContentDialog();
                    editStarted.Title = "File exists";
                    editStarted.Content = "File \"[Grayscaled] " + Path.GetFileNameWithoutExtension(inputFile.Path) + ".jpg\" already exists.";
                    editStarted.CloseButtonText = "OK";

                    await MainPage.contentDialogSemaphore.WaitAsync();
                    await editStarted.ShowAsync();
                    MainPage.contentDialogSemaphore.Release();

                    return;
                }
            }
        }

        private async void ProgressChanged(double x)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                pb.Value = x;
            });
        }

        public StorageFile GetStorageFile()
        {
            return inputFile;
        }

        public int GetParallelism()
        {
            return paralleismLevel;
        }
        
        private async void Grayscale()
        {
            currentState = EditState.Running;
            GrayedBitmap = await grayscale.ConvertAsync(this);
        }

        public CancellationTokenSource GetCancellationToken()
        {
            return cancellationTokenSource;
        }

        public async void Start()
        {
            if (currentState.Equals(EditState.Ready))
                Grayscale();

            else if (currentState.Equals(EditState.Paused))
            {
                grayscale.pauseSemaphore.Release();
                currentState = EditState.Running;
            }

            else if (currentState.Equals(EditState.Cancelled))
            {
                GrayedBitmap = null;
                ProgressChanged(0);
                SetNewCTS();
                currentState = EditState.Ready;
                Start();
            }

            else
            {
                ContentDialog editStarted = new ContentDialog();

                if (currentState.Equals(EditState.Finished))
                {
                    editStarted.Title = "Edit completed";
                    editStarted.Content = "Grayscaling completed. Please save file if needed.";
                }

                else if (currentState.Equals(EditState.Running))
                {
                    editStarted.Title = "Edit in progress";
                    editStarted.Content = "Grayscaling in progress. Please wait.";
                }

                editStarted.CloseButtonText = "OK";
                await MainPage.contentDialogSemaphore.WaitAsync();
                await editStarted.ShowAsync();
                MainPage.contentDialogSemaphore.Release();

                return;
            }
        }

        public async void SetState(EditState state)
        {
            currentState = state;

            if (state.Equals(EditState.Finished))
            {
                ImageEdit next = MainPage.GetNext();
                await MainPage.counterSemaphore.WaitAsync();
                if (MainPage.taskedCounter > 0)
                    Interlocked.Decrement(ref MainPage.taskedCounter);
                MainPage.counterSemaphore.Release();

                if (next != null)
                {
                    CoreInput setCores = new CoreInput(next);
                    await MainPage.contentDialogSemaphore.WaitAsync();
                    await setCores.ShowAsync();
                    MainPage.contentDialogSemaphore.Release();

                    await MainPage.contentDialogSemaphore.WaitAsync();
                    Interlocked.Increment(ref MainPage.taskedCounter);
                    next.Start();
                    MainPage.contentDialogSemaphore.Release();
                }
            }

            return;
        }

        public String GetName()
        {
            return Path.GetFileName(inputFile.Path);
        }

        public void SetCores(int cores)
        {
            coreLimit.Text = cores.ToString();
            coreLimit.IsEnabled = false;
            paralleismLevel = cores;
        }

        public void SetStorageFile(StorageFile sf)
        {
            inputFile = sf;
        }

        public EditState GetState()
        {
            return currentState;
        }

        public void SetNewCTS()
        {
            cancellationTokenSource = new CancellationTokenSource();
        }

        public Grayscale GetGrayscale()
        {
            return grayscale;
        }

        public XElement ToXElement()
        {
            cancellationTokenSource.Cancel();
            currentState = EditState.Ready;

            XElement returnElement = new XElement("ImageEdit",
                                            new XElement("StorageFile", inputFile.Path),
                                            new XElement("ParallelLimit", paralleismLevel),
                                            new XElement("State", currentState));
            return returnElement;
        }

        public void SetParallelLimit(int x)
        {
            if (x <= 0)
                return;

            paralleismLevel = x;
            coreLimit.Text = x.ToString();
            coreLimit.IsEnabled = false;
        }
    }
}
