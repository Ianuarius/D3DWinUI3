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
    public sealed partial class MainWindow : Window
    {
        private ID3D11Device device;
        private ID3D11DeviceContext deviceContext;
        private IDXGIDevice dxgiDevice;
        private ID3D11Debug iD3D11Debug;
        private IDXGISwapChain1 swapChain;
        private ID3D11Texture2D backBuffer;
        private ID3D11RenderTargetView renderTargetView;
        private ID3D11VertexShader vertexShader;
        private ID3D11PixelShader pixelShader;
        private ID3D11InputLayout inputLayout;
        private ID3D11RasterizerState rasterizerState;
        private ID3D11DepthStencilState depthStencilState;
        private ID3D11DepthStencilView depthStencilView;
        private ID3D11Buffer vertexBuffer;
        private ID3D11Buffer indexBuffer;
        private ID3D11Buffer constantBuffer;
        private BufferDescription instanceBufferDescription;
        private ID3D11Buffer instanceBuffer;
        private Vortice.WinUI.ISwapChainPanelNative swapChainPanel;
        private DispatcherTimer timer;
        private ID3D11ShaderResourceView brushSRV;
        private ID3D11SamplerState samplerState;

        private Viewport viewport;
        private Color4 canvasColor;
        private Color4 brushColor;
        private Vertex[] vertexArray;
        private uint[] indicesArray;
        private Matrix4x4 worldMatrix;
        private Matrix4x4 projectionMatrix;
        private Matrix4x4 viewMatrix;
        private float desiredWorldWidth;
        private float desiredWorldHeight;

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
            vertexShader.Dispose();
            pixelShader.Dispose();
            vertexBuffer.Dispose();
            indexBuffer.Dispose();
            constantBuffer.Dispose();
            swapChainPanel.Dispose();
            depthStencilState.Dispose();
            depthStencilView.Dispose();
            rasterizerState.Dispose();
            brushSRV.Dispose();
            samplerState.Dispose();

            // iD3D11Debug.ReportLiveDeviceObjects(ReportLiveDeviceObjectFlags.Detail | ReportLiveDeviceObjectFlags.IgnoreInternal);
            iD3D11Debug.Dispose();
        }

        public void InitializeDirectX()
        {
            canvasColor = new Color4(1.0f, 1.0f, 1.0f, 1.0f);
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
            vertexArray = new Vertex[]
            {
                new Vertex() { Position = new Vector3(-1.0f,  1.0f, 0.0f), UV = new Vector2(0.0f, 0.0f) },
                new Vertex() { Position = new Vector3(1.0f,  1.0f, 0.0f), UV = new Vector2(1.0f, 0.0f) },
                new Vertex() { Position = new Vector3(1.0f, -1.0f, 0.0f), UV = new Vector2(1.0f, 1.0f) },
                new Vertex() { Position = new Vector3(-1.0f, -1.0f, 0.0f), UV = new Vector2(0.0f, 1.0f) }
            };

            indicesArray = new uint[]
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
        }

        private void CreateShaders()
        {
            string vertexShaderFile = Path.Combine(AppContext.BaseDirectory, "VertexShader.hlsl");
            string pixelShaderFile = Path.Combine(AppContext.BaseDirectory, "PixelShader.hlsl");

            var vertexEntryPoint = "VS";
            var vertexProfile = "vs_5_0";
            ReadOnlyMemory<byte> vertexShaderByteCode = Compiler.CompileFromFile(vertexShaderFile, vertexEntryPoint, vertexProfile);

            var pixelEntryPoint = "PS";
            var pixelProfile = "ps_5_0";
            ReadOnlyMemory<byte> pixelShaderByteCode = Compiler.CompileFromFile(pixelShaderFile, pixelEntryPoint, pixelProfile);

            vertexShader = device.CreateVertexShader(vertexShaderByteCode.Span);
            pixelShader = device.CreatePixelShader(pixelShaderByteCode.Span);

            InputElementDescription[] inputElements = new InputElementDescription[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0, InputClassification.PerVertexData, 0),
                new InputElementDescription("POSITION", 1, Format.R32G32_Float, 0, 1, InputClassification.PerInstanceData, 1),
                new InputElementDescription("TEXCOORD", 1, Format.R32G32_Float, 8, 1, InputClassification.PerInstanceData, 1)
            };
            inputLayout = device.CreateInputLayout(inputElements, vertexShaderByteCode.Span);

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
                    ByteWidth = sizeof(Vertex) * vertexArray.Length,
                    BindFlags = BindFlags.VertexBuffer,
                    CPUAccessFlags = CpuAccessFlags.None
                };
                using DataStream dsVertex = DataStream.Create(vertexArray, true, true);
                vertexBuffer = device.CreateBuffer(vertexBufferDesc, dsVertex);
            }

            BufferDescription indexBufferDesc = new BufferDescription
            {
                Usage = ResourceUsage.Default,
                ByteWidth = sizeof(uint) * indicesArray.Length,
                BindFlags = BindFlags.IndexBuffer,
                CPUAccessFlags = CpuAccessFlags.None,
            };
            using DataStream dsIndex = DataStream.Create(indicesArray, true, true);
            indexBuffer = device.CreateBuffer(indexBufferDesc, dsIndex);

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

            int vertexStride = Marshal.SizeOf<Vertex>();
            int instanceStride = Marshal.SizeOf<InstanceData>();
            int offset = 0;
            deviceContext.IASetVertexBuffers(0, new[] { vertexBuffer, instanceBuffer }, new[] { vertexStride, instanceStride }, new[] { offset, offset });

            deviceContext.IASetIndexBuffer(indexBuffer, Format.R32_UInt, 0);
            deviceContext.IASetInputLayout(inputLayout);
            inputLayout.Dispose();

            // Vertex Shader
            deviceContext.VSSetShader(vertexShader, null, 0);
            deviceContext.VSSetConstantBuffers(0, new[] { constantBuffer });

            // Rasterizer Stage
            deviceContext.RSSetViewports(new Viewport[] { viewport });
            deviceContext.RSSetState(rasterizerState);

            // Pixel Shader
            deviceContext.PSSetShader(pixelShader, null, 0);
            deviceContext.PSSetConstantBuffer(0, constantBuffer);
            deviceContext.PSSetShaderResources(0, 1, new[] { brushSRV });
            deviceContext.PSSetSamplers(0, new[] { samplerState });

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
            deviceContext.ClearDepthStencilView(depthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
            deviceContext.OMSetRenderTargets(renderTargetView, depthStencilView);
            deviceContext.ClearRenderTargetView(renderTargetView, canvasColor);
            deviceContext.DrawIndexedInstanced(indicesArray.Length, brushStamps.Count, 0, 0, 0);

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