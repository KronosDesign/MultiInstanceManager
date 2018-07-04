using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace KronosManager
{
    public partial class MainWindow
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

        [DllImport("dwmapi.dll")]
        private static extern int DwmUnregisterThumbnail(IntPtr thumb);

        [DllImport("dwmapi.dll")]
        private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out PSIZE size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmUpdateThumbnailProperties(IntPtr hThumb, ref DWM_THUMBNAIL_PROPERTIES props);

        [DllImport("user32.dll")]
        private static extern ulong GetWindowLongA(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int EnumWindows(EnumWindowsCallback lpEnumFunc, int lParam);
        private delegate bool EnumWindowsCallback(IntPtr hwnd, int lParam);

        [DllImport("user32.dll")]
        private static extern void GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static readonly int GWL_STYLE = -16;

        private static readonly int DWM_TNP_VISIBLE = 0x8;
        private static readonly int DWM_TNP_RECTDESTINATION = 0x1;
        private static readonly int DWM_TNP_RECTSOURCE = 0x2;
        private static readonly int DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

        private static readonly ulong WS_VISIBLE = 0x10000000L;
        private static readonly ulong WS_BORDER = 0x00800000L;
        private static readonly ulong TARGETWINDOW = WS_BORDER | WS_VISIBLE;

        private static readonly Rect CLIPPING_BOX = new Rect(0, 26, 765, 510);

        private IntPtr parentWindowHandle;
        private List<WindowInfo> windows;
        private List<WindowInfo> windowsToUnregister;

        public MainWindow()
        {
            InitializeComponent();
            windows = new List<WindowInfo>();
            windowsToUnregister = new List<WindowInfo>();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            parentWindowHandle = new WindowInteropHelper(this).Handle;

            DispatcherTimer dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += Timer_Tick;
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 250);
            dispatcherTimer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            UpdatePreviews();
        }

        private void wpParentContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePreviews();
        }

        private void RefreshWindowInfo()
        {
            windowsToUnregister = windows.ToList();
            EnumWindows(WindowInfoCallback, 0);

            foreach (WindowInfo w in windowsToUnregister.ToList())
            {
                DwmUnregisterThumbnail(w.Thumbnail);
                windows.Remove(w);
                windowsToUnregister.Remove(w);
            }
        }

        private bool WindowInfoCallback(IntPtr hwnd, int lParam)
        {
            if (parentWindowHandle != hwnd && (GetWindowLongA(hwnd, GWL_STYLE) & TARGETWINDOW) == TARGETWINDOW)
            {
                StringBuilder sb = new StringBuilder(100);
                GetWindowText(hwnd, sb, sb.Capacity);

                if (sb.ToString().Contains("XoBot ") && !sb.ToString().Contains(" - "))
                {
                    foreach (WindowInfo window in windows.ToList())
                    {
                        if (window.Handle.Equals(hwnd))
                        {
                            windowsToUnregister.Remove(window);
                            return true;
                        }
                    }

                    WindowInfo w = new WindowInfo();
                    w.Handle = hwnd;
                    w.Title = sb.ToString();
                    windows.Insert(0, w);
                }
            }

            return true;
        }

        private void UpdatePreviews()
        {
            RefreshWindowInfo();

            Size parentSize = new Size { Height = brdParentContainer.ActualHeight, Width = brdParentContainer.ActualWidth };
            parentSize.Height -= (brdParentContainer.Margin.Bottom + brdParentContainer.Margin.Top);
            parentSize.Width -= (brdParentContainer.Margin.Left + brdParentContainer.Margin.Right);

            double parentAspectRatio = parentSize.Width / parentSize.Height;
            float desiredAspectRatio = 1.5f;

            Size newSize = ComputeSize(desiredAspectRatio, windows.Count, parentSize);

            if (newSize.Width > 765 || newSize.Height > 510)
            {
                wpParentContainer.ItemWidth = 771;
                wpParentContainer.ItemHeight = 510;
            }
            else
            {
                wpParentContainer.ItemWidth = newSize.Width;
                wpParentContainer.ItemHeight = newSize.Height;
            }

            wpParentContainer.Children.Clear();

            int clientIndex = 1;
            foreach (WindowInfo w in windows)
            {
                Button button = new Button();
                button.Content = "Client " + clientIndex;
                button.MouseDoubleClick += (s, e) => {
                    SetForegroundWindow(w.Handle);
                };
                wpParentContainer.Children.Add(button);
                clientIndex++;
            }

            this.Refresh();

            int index = 0;
            foreach (WindowInfo w in windows)
            {
                Point childPoint = wpParentContainer.Children[index].TranslatePoint(new Point(0, 0), this);
                Rect placeholderRect = new Rect(childPoint.X, childPoint.Y, wpParentContainer.ItemWidth, wpParentContainer.ItemHeight);
                DrawThumbnail(w, placeholderRect);
                index++;
            }
        }

        private void DrawThumbnail(WindowInfo client, Rect rect)
        {
            DWM_THUMBNAIL_PROPERTIES props = new DWM_THUMBNAIL_PROPERTIES();
            props.dwFlags = DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_SOURCECLIENTAREAONLY;
            props.fVisible = true;
            props.fSourceClientAreaOnly = true;
            props.rcDestination = new DWMRect((int)rect.Left, (int)rect.Top, (int)rect.Right, (int)rect.Bottom);
            props.rcSource = new DWMRect((int)CLIPPING_BOX.Left, (int)CLIPPING_BOX.Top, (int)CLIPPING_BOX.Right, (int)CLIPPING_BOX.Bottom);

            if (client.Thumbnail != IntPtr.Zero)
            {
                DwmUpdateThumbnailProperties(client.Thumbnail, ref props);
                return;
            }

            int hResult = DwmRegisterThumbnail(parentWindowHandle, client.Handle, out client.Thumbnail);
            if (hResult == 0)
            {
                DwmUpdateThumbnailProperties(client.Thumbnail, ref props);
            }
        }
        
        private Size ComputeSize(double DesiredAspectRatio, int NumRectangles, Size ParentSize)
        {
            double VerticalScale;
            double HorizontalScale;
            double numColumns;
            double highNumRows;
            double lowNumRows;
            double lowBoundColumns;
            double highBoundColumns;

            Size newSize = new Size();
            Size rectangleSize = new Size();

            rectangleSize.Width = DesiredAspectRatio;
            rectangleSize.Height = 1;

            numColumns = Math.Sqrt((NumRectangles * rectangleSize.Height * ParentSize.Width) / (ParentSize.Height * rectangleSize.Width));

            lowBoundColumns = Math.Floor(numColumns);
            highBoundColumns = Math.Ceiling(numColumns);
            
            lowNumRows = Math.Ceiling(NumRectangles / lowBoundColumns);
            highNumRows = Math.Ceiling(NumRectangles / highBoundColumns);
            
            VerticalScale = ParentSize.Height / lowNumRows * rectangleSize.Height;
            HorizontalScale = ParentSize.Width / (highBoundColumns * rectangleSize.Width);
            
            double MaxHorizontalArea = (HorizontalScale * rectangleSize.Width) * ((HorizontalScale * rectangleSize.Width) / DesiredAspectRatio);
            double MaxVerticalArea = (VerticalScale * rectangleSize.Height) * ((VerticalScale * rectangleSize.Height) * DesiredAspectRatio);


            if (MaxHorizontalArea >= MaxVerticalArea)
            {
                newSize.Width = ParentSize.Width / highBoundColumns;
                newSize.Height = newSize.Width / DesiredAspectRatio;
                
                if (newSize.Height * Math.Ceiling(NumRectangles / highBoundColumns) > ParentSize.Height)
                {
                    double newHeight = ParentSize.Height / highNumRows;
                    double newWidth = newHeight * DesiredAspectRatio;
                    
                    if (newWidth * NumRectangles < ParentSize.Width)
                    {
                        newWidth = ParentSize.Width / Math.Ceiling(numColumns++);
                        newHeight = newWidth / DesiredAspectRatio;
                        
                        while (newWidth * NumRectangles > ParentSize.Width)
                        {
                            newWidth = ParentSize.Width / Math.Ceiling(numColumns++);
                            newHeight = newWidth / DesiredAspectRatio;
                        }
                        
                        if (newHeight > ParentSize.Height)
                        {
                            newHeight = ParentSize.Height;
                            newWidth = newHeight * DesiredAspectRatio;
                        }
                    }
                    
                    double currentCols = Math.Floor(ParentSize.Width / newWidth);
                    double currentRows = Math.Ceiling(NumRectangles / currentCols);

                    if ((newWidth * currentCols) < ParentSize.Width && (newHeight * Math.Ceiling(NumRectangles / currentCols)) < ParentSize.Height)
                    {
                        newWidth = ParentSize.Width / currentCols;
                        newHeight = newSize.Width / DesiredAspectRatio;
                        
                        if (newHeight * Math.Ceiling(NumRectangles / currentCols) > ParentSize.Height)
                        {
                            newHeight = ParentSize.Height / currentRows;
                            newWidth = newHeight * DesiredAspectRatio;
                        }
                    }
                    
                    newSize.Height = newHeight;
                    newSize.Width = newWidth;
                }

            }
            else
            {
                newSize.Height = ParentSize.Height / lowNumRows;
                newSize.Width = newSize.Height * DesiredAspectRatio;
            }
            
            return newSize;
        }

        internal class WindowInfo
        {
            public string Title;
            public IntPtr Handle;
            public IntPtr Thumbnail;

            public override string ToString()
            {
                return Title;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public DWMRect rcDestination;
            public DWMRect rcSource;
            public byte opacity;
            public bool fVisible;
            public bool fSourceClientAreaOnly;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DWMRect
        {
            internal DWMRect(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PSIZE
        {
            public int x;
            public int y;
        }
    }
}

public static class ExtensionMethods
{
    public static void Refresh(this UIElement uiElement)
    {
        uiElement.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => { })).Wait();
    }
}