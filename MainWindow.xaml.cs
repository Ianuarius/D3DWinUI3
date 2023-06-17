using Assimp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using SharpGen.Runtime;
using System;
using System.Collections.Generic;
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
using Windows.ApplicationModel.VoiceCommands;
using Matrix4x4 = System.Numerics.Matrix4x4;

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
        private Vortice.WinUI.ISwapChainPanelNative swapChainPanel;
        private DispatcherTimer timer;
        private AssimpContext importer;

        private Viewport viewport;
        private Color4 canvasColor;
        private Matrix4x4 worldMatrix;
        private Matrix4x4 projectionMatrix;
        private Matrix4x4 viewMatrix;
        private List<Vertex> vertices;
        private List<uint> indices;
        private Mesh mesh;
        private int stride;
        private int offset;

        [StructLayout(LayoutKind.Sequential)]
        public struct Vertex
        {
            public Vector3 Position;
            public Vector3 Normal;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        struct ConstantBufferData
        {
            public Matrix4x4 WorldViewProjection;
            public Matrix4x4 World;
            public Vector4 LightPosition;
        }

        public MainWindow()
        {
            this.InitializeComponent();
            timer = new DispatcherTimer();
            timer.Tick += Timer_Tick;
            timer.Interval = TimeSpan.FromMilliseconds(1000 / 60);
            unsafe
            {
                stride = sizeof(Vertex);
                offset = 0;
            }
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
            importer.Dispose();
            rasterizerState.Dispose();

            // iD3D11Debug.ReportLiveDeviceObjects(ReportLiveDeviceObjectFlags.Detail | ReportLiveDeviceObjectFlags.IgnoreInternal);
            iD3D11Debug.Dispose();
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

        private void SwapChainCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            CreateSwapChain();
            LoadModels();
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
                Format = Format.D24_UNorm_S8_UInt,  // 24 bits for depth, 8 bits for stencil
                SampleDescription = new SampleDescription(1, 0),  // Adjust as needed
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

        private void LoadModels()
        {
            importer = new AssimpContext();
            string modelFile = Path.Combine(AppContext.BaseDirectory, "Monkey.fbx");
            Scene model = importer.ImportFile(modelFile, PostProcessPreset.TargetRealTimeMaximumQuality);

            mesh = model.Meshes[0];
            vertices = new List<Vertex>();

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Vector3D vertex = mesh.Vertices[i];
                Vector3D normal = mesh.Normals[i];

                Vertex newVertex;
                newVertex.Position = new Vector3(vertex.X, vertex.Z, -vertex.Y);
                newVertex.Normal = new Vector3(normal.X, normal.Z, -normal.Y);

                vertices.Add(newVertex);
            }

            float aspectRatio = (float)SwapChainCanvas.Width / (float)SwapChainCanvas.Height;
            float fov = 90.0f * (float)Math.PI / 180.0f;
            float nearPlane = 0.1f;
            float farPlane = 100.0f;
            projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspectRatio, nearPlane, farPlane);

            Vector3 cameraPosition = new Vector3(0.0f, 0.2f, -2.5f);
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
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0)
            };
            inputLayout = device.CreateInputLayout(inputElements, vertexShaderByteCode.Span);

            RasterizerDescription rasterizerStateDescription = new RasterizerDescription(CullMode.Back, FillMode.Solid)
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
                Vertex[] vertexArray = vertices.ToArray();
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

            indices = new List<uint>();
            foreach (Face face in mesh.Faces)
            {
                indices.AddRange(face.Indices.Select(index => (uint)index));
            }

            uint[] indicesArray = indices.ToArray();
            BufferDescription indexBufferDesc = new BufferDescription
            {
                Usage = ResourceUsage.Default,
                ByteWidth = sizeof(uint) * indicesArray.Length,
                BindFlags = BindFlags.IndexBuffer,
                CPUAccessFlags = CpuAccessFlags.None,
            };
            using DataStream dsIndex = DataStream.Create(indicesArray, true, true);
            indexBuffer = device.CreateBuffer(indexBufferDesc, dsIndex);

            var constantBufferDescription = new BufferDescription(Marshal.SizeOf<ConstantBufferData>(), BindFlags.ConstantBuffer);
            constantBuffer = device.CreateBuffer(constantBufferDescription);
        }

        public void SetRenderState()
        {
            // Input Assembler
            deviceContext.IASetInputLayout(inputLayout);
            deviceContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            deviceContext.IASetVertexBuffers(0, new[] { vertexBuffer }, new[] { stride }, new[] { offset });
            deviceContext.IASetIndexBuffer(indexBuffer, Format.R32_UInt, 0);
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

            // Output Merger
            deviceContext.OMSetDepthStencilState(depthStencilState, 1);
        }

        private void Timer_Tick(object sender, object e)
        {
            Update();
            Draw();
        }

        private void Update()
        {
            lightPosition = new Vector3(lightX, lightY, lightZ);
            float angle = 0.05f;
            worldMatrix = worldMatrix * Matrix4x4.CreateRotationY(angle);
            Matrix4x4 worldViewProjectionMatrix = worldMatrix * (viewMatrix * projectionMatrix);

            ConstantBufferData data = new ConstantBufferData();
            data.WorldViewProjection = worldViewProjectionMatrix;
            data.World = worldMatrix;
            data.LightPosition = new Vector4(lightPosition, 1);

            deviceContext.UpdateSubresource(data, constantBuffer);
        }

        private void Draw()
        {
            deviceContext.ClearDepthStencilView(depthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
            deviceContext.OMSetRenderTargets(renderTargetView, depthStencilView);
            deviceContext.ClearRenderTargetView(renderTargetView, canvasColor);
            deviceContext.DrawIndexed(indices.Count, 0, 0);
            swapChain.Present(1, PresentFlags.None);
        }

        float lightX = 0.0f; // -10 right, 10 left 
        float lightY = 0.0f; // -10 down, 10 up
        float lightZ = 0.0f; // -10 near, 10 far
        Vector3 lightPosition;

        private void SliderX_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            lightX = (float)e.NewValue;
        }

        private void SliderY_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            lightY = (float)e.NewValue;
        }

        private void SliderZ_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            lightZ = (float)e.NewValue;
        }
    }
}