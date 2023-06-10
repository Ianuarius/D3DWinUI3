using Microsoft.UI.Xaml;
using SharpGen.Runtime;
using System;
using System.IO;
using Vortice;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D11.Debug;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace D3DWinUI3
{

    public sealed partial class MainWindow : Window
    {
        private DispatcherTimer timer;
        private ID3D11Device device;
        private ID3D11DeviceContext deviceContext;
        private IDXGIDevice dxgiDevice;
        private IDXGISwapChain1 swapChain;
        private ID3D11Texture2D backBuffer;
        private ID3D11RenderTargetView renderTargetView;
        private Vortice.WinUI.ISwapChainPanelNative swapChainPanel;
        private ID3D11VertexShader vertexShader;
        private ID3D11PixelShader pixelShader;
        private ID3D11Buffer vertexBuffer;
        private ID3D11Buffer indexBuffer;
        private ID3D11InputLayout inputLayout;
        private ID3D11Debug iD3D11Debug;

        private Viewport viewport;
        private Color4 canvasColor;
        private int stride = sizeof(float) * 3;
        private int offset = 0;

        public MainWindow()
        {
            this.InitializeComponent();
            timer = new DispatcherTimer();
            timer.Tick += Timer_Tick;
            timer.Interval = TimeSpan.FromMilliseconds(1000 / 60);
            InitializeDirectX();
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            deviceContext.ClearState();
            deviceContext.Flush();

            device.Dispose();
            deviceContext.Dispose();
            swapChain.Dispose();
            backBuffer.Dispose();
            renderTargetView.Dispose();
            vertexShader.Dispose();
            pixelShader.Dispose();
            vertexBuffer.Dispose();
            indexBuffer.Dispose();
            swapChainPanel.Dispose();

            // iD3D11Debug.ReportLiveDeviceObjects(ReportLiveDeviceObjectFlags.Detail | ReportLiveDeviceObjectFlags.IgnoreInternal);
            iD3D11Debug.Dispose();
        }

        private void SwapChainCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            CreateSwapChain();
            CreateResources();
            SetRenderState();
            timer.Start();
        }

        public void InitializeDirectX()
        {
            canvasColor = new Color4(1.0f, 1.0f, 1.0f, 1.0f);

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
            DeviceCreationFlags deviceCreationFlags = DeviceCreationFlags.BgraSupport;

#if DEBUG
            deviceCreationFlags |= DeviceCreationFlags.Debug;
#endif

            D3D11.D3D11CreateDevice(null, DriverType.Hardware, deviceCreationFlags, featureLevels, out tempDevice, out tempContext).CheckError();
            device = tempDevice;
            deviceContext = tempContext;
            iD3D11Debug = device.QueryInterfaceOrNull<ID3D11Debug>();
            dxgiDevice = device.QueryInterface<IDXGIDevice>();
        }

        public void CreateSwapChain()
        {
            ComObject comObject = new ComObject(SwapChainCanvas);
            swapChainPanel = comObject.QueryInterfaceOrNull<Vortice.WinUI.ISwapChainPanelNative>();
            comObject.Dispose();

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
            dxgiAdapter.Dispose();
            dxgiFactory2.Dispose();
            dxgiDevice.Dispose();

            backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
            renderTargetView = device.CreateRenderTargetView(backBuffer);
            IDXGISurface dxgiSurface = backBuffer.QueryInterface<IDXGISurface>();
            swapChainPanel.SetSwapChain(swapChain);
            dxgiSurface.Dispose();

            viewport = new Viewport
            {
                X = 0.0f,
                Y = 0.0f,
                Width = (float)SwapChainCanvas.Width,
                Height = (float)SwapChainCanvas.Height,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };
        }

        public void CreateResources()
        {
            string shaderFile = Path.Combine(AppContext.BaseDirectory, "Shader.hlsl");

            var vertexEntryPoint = "VS";
            var vertexProfile = "vs_5_0";
            ReadOnlyMemory<byte> vertexShaderByteCode = Compiler.CompileFromFile(shaderFile, vertexEntryPoint, vertexProfile);

            var pixelEntryPoint = "PS";
            var pixelProfile = "ps_5_0";
            ReadOnlyMemory<byte> pixelShaderByteCode = Compiler.CompileFromFile(shaderFile, pixelEntryPoint, pixelProfile);

            vertexShader = device.CreateVertexShader(vertexShaderByteCode.Span);
            pixelShader = device.CreatePixelShader(pixelShaderByteCode.Span);

            float[] vertices = new float[]
            {
                0f, 0.5f, 0f, // Top-center
                0.5f, -0.5f, 0f, // Bottom-right
                -0.5f, -0.5f, 0f, // Bottom-left
            };

            BufferDescription vertexBufferDesc = new BufferDescription()
            {
                Usage = ResourceUsage.Default,
                ByteWidth = sizeof(float) * 3 * vertices.Length,
                BindFlags = BindFlags.VertexBuffer,
                CPUAccessFlags = CpuAccessFlags.None
            };
            using DataStream dsVertex = DataStream.Create(vertices, true, true);
            vertexBuffer = device.CreateBuffer(vertexBufferDesc, dsVertex);

            int[] indices = new int[]
            {
                0, 1, 2,
            };

            BufferDescription indexBufferDesc = new BufferDescription
            {
                Usage = ResourceUsage.Default,
                ByteWidth = sizeof(uint) * indices.Length,
                BindFlags = BindFlags.IndexBuffer,
                CPUAccessFlags = CpuAccessFlags.None,
            };
            using DataStream dsIndex = DataStream.Create(indices, true, true);
            indexBuffer = device.CreateBuffer(indexBufferDesc, dsIndex);

            InputElementDescription[] inputElements = new InputElementDescription[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            };
            inputLayout = device.CreateInputLayout(inputElements, vertexShaderByteCode.Span);
        }

        public void SetRenderState()
        {
            deviceContext.VSSetShader(vertexShader, null, 0);
            deviceContext.PSSetShader(pixelShader, null, 0);

            deviceContext.IASetVertexBuffers(0, new[] { vertexBuffer }, new[] { stride }, new[] { offset });
            deviceContext.IASetIndexBuffer(indexBuffer, Format.R32_UInt, 0);
            deviceContext.IASetInputLayout(inputLayout);
            inputLayout.Dispose();
            deviceContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

            deviceContext.RSSetViewports(new Viewport[] { viewport });
        }

        private void Timer_Tick(object sender, object e)
        {
            Draw();
        }

        private void Draw()
        {
            deviceContext.OMSetRenderTargets(renderTargetView);
            deviceContext.ClearRenderTargetView(renderTargetView, canvasColor);
            deviceContext.DrawIndexed(3, 0, 0);
            swapChain.Present(1, PresentFlags.None);
        }
    }
}
