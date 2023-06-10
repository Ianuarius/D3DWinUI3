using Microsoft.UI.Xaml;
using System;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace D3DWinUI3
{

    public sealed partial class MainWindow : Window
    {
        private DispatcherTimer timer;
        private ID3D11Device device;
        private ID3D11DeviceContext deviceContext;
        private IDXGIDevice dxgiDevice;
        private IDXGISwapChain1 swapChain;

        public MainWindow()
        {
            this.InitializeComponent();
            timer = new DispatcherTimer();
            timer.Tick += Timer_Tick;
            timer.Interval = TimeSpan.FromMilliseconds(1000 / 60);
        }

        public void InitializeDirectX()
        {
            FeatureLevel[] featureLevels = new FeatureLevel[]
            {
                FeatureLevel.Level_12_1,
                FeatureLevel.Level_12_0,
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0,
                FeatureLevel.Level_9_3,
                FeatureLevel.Level_9_2,
                FeatureLevel.Level_9_1
            };

            ID3D11Device tempDevice;
            ID3D11DeviceContext tempContext;

            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.Debug,
                featureLevels,
                out tempDevice,
                out tempContext).CheckError();
            device = tempDevice;
            deviceContext = tempContext;
            dxgiDevice = device.QueryInterface<IDXGIDevice>();
        }

        public void CreateSwapChain()
        {
            SwapChainDescription1 swapChainDesc = new SwapChainDescription1()
            {
                Stereo = false,
                Width = (int)SwapChainCanvas.Width,
                Height = (int)SwapChainCanvas.Height,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Scaling = Scaling.Stretch,
                AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
                Flags = SwapChainFlags.None,
                SwapEffect = SwapEffect.FlipSequential
            };

            IDXGIAdapter1 dxgiAdapter = dxgiDevice.GetParent<IDXGIAdapter1>();
            IDXGIFactory2 dxgiFactory2 = dxgiAdapter.GetParent<IDXGIFactory2>();
            swapChain = dxgiFactory2.CreateSwapChainForComposition(device, swapChainDesc, null);
        }

        private void Timer_Tick(object sender, object e)
        {
        }
    }
}