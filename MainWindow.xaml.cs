using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using SharpGen.Runtime;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D11.Debug;
using Vortice.DXGI;
using Vortice.Mathematics;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Point = Windows.Foundation.Point;

namespace D3DWinUI3
{
    public class Layer
    {
        public ID3D11Texture2D Texture { get; set; }
        public ID3D11RenderTargetView RenderTargetView { get; set; }
        public ID3D11ShaderResourceView ShaderResourceView { get; set; }
    }

    public sealed partial class MainWindow : Window
    {
        private ID3D11Device device;
        private ID3D11DeviceContext deviceContext;
        private ID3D11Debug iD3D11Debug;
        private ID3D11RenderTargetView renderTargetView;
        private IDXGISwapChain1 swapChain;
        private ID3D11Texture2D backBuffer;
        private ID3D11Texture2D intermediateTexture1;
        private ID3D11Texture2D intermediateTexture2;
        private ID3D11RenderTargetView intermediateRTV1;
        private ID3D11RenderTargetView intermediateRTV2;
        private ID3D11ShaderResourceView intermediateSRV1;
        private ID3D11ShaderResourceView intermediateSRV2;
        private IDXGIDevice dxgiDevice;
        private ID3D11VertexShader vertexInstances;
        private ID3D11PixelShader pixelInstances;
        private ID3D11VertexShader vertexMerger;
        private ID3D11PixelShader pixelMerger;
        private ID3D11InputLayout instancesInputLayout;
        private ID3D11InputLayout mergerInputLayout;
        private ID3D11RasterizerState rasterizerState;
        private ID3D11DepthStencilState depthStencilState;
        private ID3D11DepthStencilView depthStencilView;
        private ID3D11Buffer instanceVertexBuffer;
        private ID3D11Buffer instanceIndexBuffer;
        private ID3D11Buffer fullscreenVertexBuffer;
        private ID3D11Buffer fullscreenIndexBuffer;
        private ID3D11Buffer constantBuffer;
        private BufferDescription instanceBufferDescription;
        private ID3D11Buffer instanceBuffer;
        private Vortice.WinUI.ISwapChainPanelNative swapChainPanel;
        private DispatcherTimer timer;
        private ID3D11ShaderResourceView brushSRV;
        private ID3D11SamplerState samplerState;

        private Viewport viewport;
        private Color4 brushColor;
        private Vertex[] instanceVertices;
        private Vertex[] fullscreenVertices;
        private uint[] quadIndices;
        private uint[] fullscreenIndices;
        private Matrix4x4 worldMatrix;
        private Matrix4x4 projectionMatrix;
        private Matrix4x4 viewMatrix;
        private float desiredWorldWidth;
        private float desiredWorldHeight;
        private List<Layer> layers = new List<Layer>();
        private int activeLayer;
        private Color4 colorTransparent = new Color4(0.0f, 0.0f, 0.0f, 0.0f);

        [StructLayout(LayoutKind.Sequential)]
        public struct Vertex
        {
            public Vector3 Position;
            public Vector2 UV;
        }

        private float minDistance = 80;
        private List<BrushStamp> brushStamps = new List<BrushStamp>();
        struct BrushStamp
        {
            public Point Position;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 16, Size = 32)]
        public struct InstanceData
        {
            public Vector2 Position;
            public Vector2 Scale;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        struct ConstantBufferData
        {
            public Matrix4x4 WorldViewProjection;
            public Matrix4x4 World;
            public Vector4 BrushColor;
            public Vector2 ClickPosition;
            public Vector2 padding;
        }

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
            swapChainPanel.Dispose();

            vertexInstances.Dispose();
            pixelInstances.Dispose();
            instanceVertexBuffer.Dispose();
            instanceIndexBuffer.Dispose();
            instancesInputLayout.Dispose();
            vertexMerger.Dispose();
            pixelMerger.Dispose();
            fullscreenVertexBuffer.Dispose();
            fullscreenIndexBuffer.Dispose();
            mergerInputLayout.Dispose();
            constantBuffer.Dispose();
            instanceBuffer.Dispose();

            depthStencilState.Dispose();
            depthStencilView.Dispose();
            rasterizerState.Dispose();
            intermediateRTV1.Dispose();
            intermediateRTV2.Dispose();
            intermediateTexture1.Dispose();
            intermediateTexture2.Dispose();
            intermediateSRV1.Dispose();
            intermediateSRV2.Dispose();

            brushSRV.Dispose();
            samplerState.Dispose();
            layers[activeLayer].ShaderResourceView.Dispose();
            layers[activeLayer].RenderTargetView.Dispose();
            layers[activeLayer].Texture.Dispose();

            // iD3D11Debug.ReportLiveDeviceObjects(ReportLiveDeviceObjectFlags.Detail | ReportLiveDeviceObjectFlags.IgnoreInternal);
            iD3D11Debug.Dispose();
        }

        public void InitializeDirectX()
        {
            brushColor = new Color4(1.0f, 0.0f, 0.0f, 1.0f);
            desiredWorldWidth = (float)SwapChainCanvas.Width;
            desiredWorldHeight = (float)SwapChainCanvas.Height;

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

        private void SwapChainCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            CreateSwapChain();
            CreateResources();
            CreateShaders();
            CreateBuffers();
            SetRenderState();
            timer.Start();
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


            Texture2DDescription intermediateTextureDescription = new Texture2DDescription()
            {
                Width = (int)SwapChainCanvas.Width,
                Height = (int)SwapChainCanvas.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None
            };

            intermediateTexture1 = device.CreateTexture2D(intermediateTextureDescription);
            intermediateTexture2 = device.CreateTexture2D(intermediateTextureDescription);
            intermediateRTV1 = device.CreateRenderTargetView(intermediateTexture1);
            intermediateRTV2 = device.CreateRenderTargetView(intermediateTexture2);
            intermediateSRV1 = device.CreateShaderResourceView(intermediateTexture1);
            intermediateSRV2 = device.CreateShaderResourceView(intermediateTexture2);

            Texture2DDescription depthBufferDesc = new Texture2DDescription
            {
                Width = (int)SwapChainCanvas.Width,
                Height = (int)SwapChainCanvas.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.D24_UNorm_S8_UInt,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None,
            };

            ID3D11Texture2D depthBuffer = device.CreateTexture2D(depthBufferDesc);

            DepthStencilViewDescription depthStencilViewDesc = new DepthStencilViewDescription
            {
                Format = depthBufferDesc.Format,
                ViewDimension = DepthStencilViewDimension.Texture2D,
                Flags = DepthStencilViewFlags.None,
            };

            depthStencilView = device.CreateDepthStencilView(depthBuffer, depthStencilViewDesc);
            depthBuffer.Dispose();

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

        public static (byte[], int, int) LoadBitmapData(string filePath)
        {
            Bitmap bitmap = new Bitmap(filePath);
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);

            int numBytes = bmpData.Stride * bitmap.Height;
            byte[] byteValues = new byte[numBytes];

            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, byteValues, 0, numBytes);

            bitmap.UnlockBits(bmpData);

            return (byteValues, bitmap.Width, bmpData.Stride);
        }

        private void CreateResources()
        {
            instanceVertices = new Vertex[]
            {
                new Vertex() { Position = new Vector3(-1.0f,  1.0f, 0.0f), UV = new Vector2(0.0f, 0.0f) },
                new Vertex() { Position = new Vector3( 1.0f,  1.0f, 0.0f), UV = new Vector2(1.0f, 0.0f) },
                new Vertex() { Position = new Vector3( 1.0f, -1.0f, 0.0f), UV = new Vector2(1.0f, 1.0f) },
                new Vertex() { Position = new Vector3(-1.0f, -1.0f, 0.0f), UV = new Vector2(0.0f, 1.0f) }
            };

            fullscreenVertices = new Vertex[]
            {
                new Vertex() { Position = new Vector3(-1.0f,  1.0f, 0.0f), UV = new Vector2(0.0f, 0.0f) },
                new Vertex() { Position = new Vector3( 1.0f,  1.0f, 0.0f), UV = new Vector2(1.0f, 0.0f) },
                new Vertex() { Position = new Vector3( 1.0f, -1.0f, 0.0f), UV = new Vector2(1.0f, 1.0f) },
                new Vertex() { Position = new Vector3(-1.0f, -1.0f, 0.0f), UV = new Vector2(0.0f, 1.0f) }
            };

            quadIndices = new uint[]
            {
                0, 1, 2,
                0, 2, 3
            };

            fullscreenIndices = new uint[]
            {
                0, 1, 2,
                0, 2, 3
            };

            string bitmapFile = Path.Combine(AppContext.BaseDirectory, "BrushRGBA.png");
            (byte[] bitmapData, int bitmapWidth, int bitmapStride) = LoadBitmapData(bitmapFile);
            int bitmapHeight = bitmapData.Length / bitmapStride;

            IntPtr dataPointer = Marshal.AllocHGlobal(bitmapData.Length);
            Marshal.Copy(bitmapData, 0, dataPointer, bitmapData.Length);
            SubresourceData subresourceData = new SubresourceData(dataPointer, bitmapStride, bitmapData.Length);

            Texture2DDescription brushTextureDesc = new Texture2DDescription()
            {
                Width = bitmapWidth,
                Height = bitmapHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None
            };

            ID3D11Texture2D brushTexture = device.CreateTexture2D(brushTextureDesc, subresourceData);
            Marshal.FreeHGlobal(dataPointer);

            ShaderResourceViewDescription brushSRVDesc = new ShaderResourceViewDescription()
            {
                Format = brushTextureDesc.Format,
                ViewDimension = ShaderResourceViewDimension.Texture2D,
                Texture2D = new Texture2DShaderResourceView()
                {
                    MipLevels = 1,
                    MostDetailedMip = 0
                }
            };

            brushSRV = device.CreateShaderResourceView(brushTexture, brushSRVDesc);
            brushTexture.Dispose();

            SamplerDescription samplerDescription = new SamplerDescription()
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                MipLODBias = 0,
                MaxAnisotropy = 1,
                ComparisonFunc = ComparisonFunction.Always,
                BorderColor = new Color4(0, 0, 0, 0),
                MinLOD = 0,
                MaxLOD = float.MaxValue
            };

            samplerState = device.CreateSamplerState(samplerDescription);

            BlendDescription blendDescription = new BlendDescription()
            {
                AlphaToCoverageEnable = false,
                IndependentBlendEnable = false,
            };

            blendDescription.RenderTarget[0] = new RenderTargetBlendDescription
            {
                BlendEnable = true,
                SourceBlend = Blend.One,
                DestinationBlend = Blend.InverseSourceAlpha,
                BlendOperation = BlendOperation.Add,
                SourceBlendAlpha = Blend.One,
                DestinationBlendAlpha = Blend.Zero,
                BlendOperationAlpha = BlendOperation.Add,
                RenderTargetWriteMask = ColorWriteEnable.All
            };

            ID3D11BlendState blendState = device.CreateBlendState(blendDescription);

            float[] blendFactor = new float[] { 1.0f, 1.0f, 1.0f, 1.0f };
            unsafe
            {
                fixed (float* ptr = blendFactor)
                {
                    deviceContext.OMSetBlendState(blendState, ptr, 0xffffffff);
                }
            }
            blendState.Dispose();

            float aspectRatio = (float)SwapChainCanvas.Width / (float)SwapChainCanvas.Height;
            float nearPlane = 0.1f;
            float farPlane = 100.0f;
            projectionMatrix = Matrix4x4.CreateOrthographic(desiredWorldWidth, desiredWorldHeight, nearPlane, farPlane);

            Vector3 cameraPosition = new Vector3(0.0f, 0.0f, 1.0f);
            Vector3 cameraTarget = new Vector3(0.0f, 0.0f, 0.0f);
            Vector3 cameraUp = new Vector3(0.0f, 1.0f, 0.0f);
            viewMatrix = Matrix4x4.CreateLookAt(cameraPosition, cameraTarget, cameraUp);

            worldMatrix = Matrix4x4.Identity;

            CreateLayer();
            activeLayer = 0;
        }

        public void CreateLayer()
        {
            Layer layer = new Layer();
            Texture2DDescription layerTextureDescription = new Texture2DDescription()
            {
                Width = (int)SwapChainCanvas.Width,
                Height = (int)SwapChainCanvas.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None
            };
            layer.Texture = device.CreateTexture2D(layerTextureDescription);
            layer.RenderTargetView = device.CreateRenderTargetView(layer.Texture);
            ShaderResourceViewDescription shaderResourceViewDesc = new ShaderResourceViewDescription()
            {
                Format = layerTextureDescription.Format,
                ViewDimension = ShaderResourceViewDimension.Texture2D,
                Texture2D = new Texture2DShaderResourceView()
                {
                    MipLevels = 1,
                    MostDetailedMip = 0
                }
            };
            layer.ShaderResourceView = device.CreateShaderResourceView(layer.Texture, shaderResourceViewDesc);
            layers.Add(layer);
        }

        private void CreateShaders()
        {
            var vertexEntryPoint = "VS";
            var vertexProfile = "vs_5_0";
            string vertexInstancesFile = Path.Combine(AppContext.BaseDirectory, "VertexInstances.hlsl");
            string vertexMergerFile = Path.Combine(AppContext.BaseDirectory, "VertexMerger.hlsl");

            var pixelEntryPoint = "PS";
            var pixelProfile = "ps_5_0";
            string pixelInstancesFile = Path.Combine(AppContext.BaseDirectory, "PixelInstances.hlsl");
            string pixelMergerFile = Path.Combine(AppContext.BaseDirectory, "PixelMerger.hlsl");

            ReadOnlyMemory<byte> vertexInstancesByteCode = Compiler.CompileFromFile(vertexInstancesFile, vertexEntryPoint, vertexProfile);
            ReadOnlyMemory<byte> pixelInstancesByteCode = Compiler.CompileFromFile(pixelInstancesFile, pixelEntryPoint, pixelProfile);
            ReadOnlyMemory<byte> vertexMergerByteCode = Compiler.CompileFromFile(vertexMergerFile, vertexEntryPoint, vertexProfile);
            ReadOnlyMemory<byte> pixelMergerByteCode = Compiler.CompileFromFile(pixelMergerFile, pixelEntryPoint, pixelProfile);

            vertexInstances = device.CreateVertexShader(vertexInstancesByteCode.Span);
            pixelInstances = device.CreatePixelShader(pixelInstancesByteCode.Span);
            vertexMerger = device.CreateVertexShader(vertexMergerByteCode.Span);
            pixelMerger = device.CreatePixelShader(pixelMergerByteCode.Span);

            InputElementDescription[] instancesInputElements = new InputElementDescription[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0, InputClassification.PerVertexData, 0),
                new InputElementDescription("POSITION", 1, Format.R32G32_Float, 0, 1, InputClassification.PerInstanceData, 1),
                new InputElementDescription("TEXCOORD", 1, Format.R32G32_Float, 8, 1, InputClassification.PerInstanceData, 1)
            };

            InputElementDescription[] mergerInputElements = new InputElementDescription[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0, InputClassification.PerVertexData, 0),
            };

            instancesInputLayout = device.CreateInputLayout(instancesInputElements, vertexInstancesByteCode.Span);
            mergerInputLayout = device.CreateInputLayout(mergerInputElements, vertexMergerByteCode.Span);

            RasterizerDescription rasterizerStateDescription = new RasterizerDescription(CullMode.None, FillMode.Solid)
            {
                FrontCounterClockwise = true,
                DepthBias = 0,
                DepthBiasClamp = 0f,
                SlopeScaledDepthBias = 0f,
                DepthClipEnable = true,
                ScissorEnable = false,
                MultisampleEnable = true,
                AntialiasedLineEnable = false
            };
            rasterizerState = device.CreateRasterizerState(rasterizerStateDescription);

            DepthStencilDescription depthStencilDescription = new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.LessEqual)
            {
                StencilEnable = false,
                StencilReadMask = byte.MaxValue,
                StencilWriteMask = byte.MaxValue,
                FrontFace = DepthStencilOperationDescription.Default,
                BackFace = DepthStencilOperationDescription.Default
            };
            depthStencilState = device.CreateDepthStencilState(depthStencilDescription);
        }

        private void CreateBuffers()
        {
            unsafe
            {
                BufferDescription vertexBufferDesc = new BufferDescription()
                {
                    Usage = ResourceUsage.Default,
                    ByteWidth = sizeof(Vertex) * instanceVertices.Length,
                    BindFlags = BindFlags.VertexBuffer,
                    CPUAccessFlags = CpuAccessFlags.None
                };
                using DataStream dsVertex = DataStream.Create(instanceVertices, true, true);
                instanceVertexBuffer = device.CreateBuffer(vertexBufferDesc, dsVertex);
            }

            unsafe
            {
                BufferDescription fullscreenQuadBufferDesc = new BufferDescription()
                {
                    Usage = ResourceUsage.Default,
                    ByteWidth = sizeof(Vertex) * fullscreenVertices.Length,
                    BindFlags = BindFlags.VertexBuffer,
                    CPUAccessFlags = CpuAccessFlags.None
                };
                using DataStream dsVertex = DataStream.Create(fullscreenVertices, true, true);
                fullscreenVertexBuffer = device.CreateBuffer(fullscreenQuadBufferDesc, dsVertex);
            }

            BufferDescription indexBufferDesc = new BufferDescription
            {
                Usage = ResourceUsage.Default,
                ByteWidth = sizeof(uint) * quadIndices.Length,
                BindFlags = BindFlags.IndexBuffer,
                CPUAccessFlags = CpuAccessFlags.None,
            };
            using DataStream dsIndex = DataStream.Create(quadIndices, true, true);
            instanceIndexBuffer = device.CreateBuffer(indexBufferDesc, dsIndex);

            BufferDescription fullscreenIndexBufferDesc = new BufferDescription
            {
                Usage = ResourceUsage.Default,
                ByteWidth = sizeof(uint) * fullscreenIndices.Length,
                BindFlags = BindFlags.IndexBuffer,
                CPUAccessFlags = CpuAccessFlags.None,
            };
            using DataStream dsIndex2 = DataStream.Create(fullscreenIndices, true, true);
            fullscreenIndexBuffer = device.CreateBuffer(fullscreenIndexBufferDesc, dsIndex2);

            BufferDescription constantBufferDescription = new BufferDescription(Marshal.SizeOf<ConstantBufferData>(), BindFlags.ConstantBuffer);
            constantBuffer = device.CreateBuffer(constantBufferDescription);

            int maxInstances = 20;

            instanceBufferDescription = new BufferDescription()
            {
                ByteWidth = maxInstances * Marshal.SizeOf<InstanceData>(),
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.VertexBuffer,
                CPUAccessFlags = CpuAccessFlags.Write,
                StructureByteStride = Marshal.SizeOf<InstanceData>()
            };
            instanceBuffer = device.CreateBuffer(instanceBufferDescription);
        }

        private void Timer_Tick(object sender, object e)
        {
            Update();
            Draw();
        }

        public void SetRenderState()
        {
            // Input Assembler
            deviceContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

            // Vertex Shader
            deviceContext.VSSetConstantBuffer(0, constantBuffer);

            // Rasterizer Stage
            deviceContext.RSSetViewports(new Viewport[] { viewport });
            deviceContext.RSSetState(rasterizerState);

            // Pixel Shader	
            deviceContext.PSSetConstantBuffer(0, constantBuffer);

            // Output Merger
            deviceContext.OMSetDepthStencilState(depthStencilState, 1);
        }

        private void Update()
        {
            Matrix4x4 worldViewProjectionMatrix = worldMatrix * (viewMatrix * projectionMatrix);
            ConstantBufferData data = new ConstantBufferData();
            data.BrushColor = brushColor;

            if (brushStamps.Count > 0)
            {
                MappedSubresource mappedResource = deviceContext.Map(instanceBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                IntPtr dataPtr = mappedResource.DataPointer;
                foreach (BrushStamp stamp in brushStamps)
                {
                    float scale = 100.0f;
                    Vector2 pos = ConvertMousePointTo3D(stamp.Position);
                    Vector2 scaleVector = new Vector2(scale, scale);
                    InstanceData instanceData = new InstanceData { Position = pos, Scale = scaleVector };
                    Marshal.StructureToPtr(instanceData, dataPtr, true);
                    dataPtr += Marshal.SizeOf<InstanceData>();
                }
                deviceContext.Unmap(instanceBuffer, 0);
            }
            data.WorldViewProjection = worldViewProjectionMatrix;
            data.World = worldMatrix;
            deviceContext.UpdateSubresource(data, constantBuffer);
        }

        private void Draw()
        {
            // 1. Draw Instances to intermediateRTV1
            deviceContext.VSSetShader(vertexInstances, null, 0);
            deviceContext.PSSetShader(pixelInstances, null, 0);
            deviceContext.IASetInputLayout(instancesInputLayout);

            deviceContext.ClearRenderTargetView(intermediateRTV1, colorTransparent);
            deviceContext.OMSetRenderTargets(intermediateRTV1, depthStencilView);

            int vertexStride = Marshal.SizeOf<Vertex>();
            int instanceStride = Marshal.SizeOf<InstanceData>();
            int offset = 0;
            deviceContext.IASetVertexBuffer(0, instanceVertexBuffer, vertexStride, offset);
            deviceContext.IASetVertexBuffer(1, instanceBuffer, instanceStride, offset);
            deviceContext.IASetIndexBuffer(instanceIndexBuffer, Format.R32_UInt, 0);

            deviceContext.PSSetShaderResource(0, brushSRV);
            deviceContext.PSSetSampler(0, samplerState);
            deviceContext.DrawIndexedInstanced(quadIndices.Length, brushStamps.Count, 0, 0, 0);

            // 2. Merge new instances with old layer texture
            deviceContext.VSSetShader(vertexMerger, null, 0);
            deviceContext.PSSetShader(pixelMerger, null, 0);
            deviceContext.IASetInputLayout(mergerInputLayout);
            deviceContext.IASetVertexBuffer(0, fullscreenVertexBuffer, vertexStride, 0);
            deviceContext.IASetIndexBuffer(fullscreenIndexBuffer, Format.R32_UInt, 0);

            deviceContext.PSSetShaderResource(0, null);
            deviceContext.PSSetShaderResource(1, null);

            deviceContext.OMSetRenderTargets(intermediateRTV2, depthStencilView);

            if (brushStamps.Count > 0)
            {
                deviceContext.PSSetShaderResource(0, layers[activeLayer].ShaderResourceView);
                deviceContext.PSSetShaderResource(1, intermediateSRV1);
                deviceContext.DrawIndexed(6, 0, 0);

                using (ID3D11Resource resource = layers[activeLayer].RenderTargetView.Resource)
                {
                    using (ID3D11Resource resource2 = intermediateRTV2.Resource)
                    {
                        deviceContext.CopyResource(resource, resource2);
                    }
                }
                deviceContext.ClearRenderTargetView(intermediateRTV2, colorTransparent);
            }

            // 3. Merge Layers together
            for (int i = 0; i < layers.Count; i++)
            {
                deviceContext.PSSetShaderResource(0, null);
                deviceContext.PSSetShaderResource(1, null);
                if (i % 2 == 0)
                {
                    deviceContext.ClearRenderTargetView(intermediateRTV1, colorTransparent);
                    deviceContext.OMSetRenderTargets(intermediateRTV1, depthStencilView);
                    deviceContext.PSSetShaderResource(0, intermediateSRV2);
                    deviceContext.PSSetShaderResource(1, layers[i].ShaderResourceView);
                    deviceContext.DrawIndexed(6, 0, 0);
                }
                else
                {
                    deviceContext.ClearRenderTargetView(intermediateRTV2, colorTransparent);
                    deviceContext.OMSetRenderTargets(intermediateRTV2, depthStencilView);
                    deviceContext.PSSetShaderResource(0, intermediateSRV1);
                    deviceContext.PSSetShaderResource(1, layers[i].ShaderResourceView);
                    deviceContext.DrawIndexed(6, 0, 0);
                }
            }

            // 4. Copy merged layers to backbuffer
            if ((layers.Count - 1) % 2 == 0)
            {
                using (ID3D11Resource resource = renderTargetView.Resource)
                {
                    using (ID3D11Resource resource2 = intermediateRTV1.Resource)
                    {
                        deviceContext.CopyResource(resource, resource2);
                    }
                }
            }
            else
            {
                using (ID3D11Resource resource = renderTargetView.Resource)
                {
                    using (ID3D11Resource resource2 = intermediateRTV2.Resource)
                    {
                        deviceContext.CopyResource(resource, resource2);
                    }
                }
            }

            brushStamps.Clear();
            swapChain.Present(1, PresentFlags.None);
        }

        private Vector2 ConvertMousePointTo3D(Point mousePoint)
        {
            float worldX = (float)((mousePoint.X / SwapChainCanvas.Width) * desiredWorldWidth - desiredWorldWidth / 2f);
            float worldY = (float)(desiredWorldHeight / 2f - ((float)mousePoint.Y / SwapChainCanvas.Height) * desiredWorldHeight);
            return new Vector2(worldX, worldY);
        }

        private bool isDrawing = false;

        private void SwapChainCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            isDrawing = true;
        }

        private void SwapChainCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (isDrawing)
            {
                Point currentPos = e.GetCurrentPoint(SwapChainCanvas).Position;
                if (brushStamps.Count == 0 || Distance(brushStamps.Last().Position, currentPos) >= minDistance)
                {
                    brushStamps.Add(new BrushStamp { Position = currentPos });
                }
            }
        }

        private double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void SwapChainCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            isDrawing = false;
        }
    }
}