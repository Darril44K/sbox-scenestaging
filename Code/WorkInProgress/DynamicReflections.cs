using Sandbox.Rendering;

namespace Sandbox;

[Title( "Dynamic Reflections (SSR)" )]
[Category( "Post Processing" )]
[Icon( "local_mall" )]
[Hide]
public class DynamicReflections : PostProcess, Component.ExecuteInEditor
{
    Rendering.CommandList Commands;
    Rendering.CommandList FBCopyCommand;
    int Frame;

    Texture BlueNoise { get; set; } = Texture.Load( FileSystem.Mounted, "textures/dev/blue_noise_256.vtex" );

    /// <summary>
    /// Until which roughness value the effect should be applied
    /// </summary>
    [Property, Range( 0.0f, 0.9f )] float RoughnessCutoff { get; set; } = 0.5f;

    /// <summary>
    /// Quality of the effect ( Full res, Half res, Quarter res )
    /// Default half res
    /// </summary>
    [Property] int Quality = 1;

    /// <summary>
    /// Number of samples per pixel
    /// </summary>
    [Property, Hide] int SamplesPerPixel = 1;

    enum Passes
    {
        //ClassifyTiles,
        Intersect,
        DenoiseReproject,
        DenoisePrefilter,
        DenoiseResolveTemporal
    }

    protected override void OnEnabled()
    {
        Commands = new Rendering.CommandList( "Dynamic Reflections" );
        FBCopyCommand = new Rendering.CommandList( "Get Last Frame Color" );
        
        OnDirty();

        Camera.AddCommandList( FBCopyCommand, Rendering.Stage.AfterOpaque, int.MaxValue );
        Camera.AddCommandList( Commands, Rendering.Stage.AfterDepthPrepass, int.MaxValue );

    }

    protected override void OnDisabled()
    {
        Camera.RemoveCommandList( FBCopyCommand );
        Camera.RemoveCommandList( Commands );

        Commands = null;
        FBCopyCommand = null;

        Frame = 0;
    }

    protected override void OnUpdate()
    {
        Rebuild();
    }

    void Rebuild()
    {
        if ( Commands is null )
            return;

        FBCopyCommand.Reset();
        Commands.Reset();
        
		bool pingPong = (Frame++ % 2) == 0;
        int downsampleRatio = (int)Math.Pow( 2, 0 );
        
        Commands.Set( "BlueNoiseIndex", BlueNoise.Index );

        var PreviousFrameColorRT = FBCopyCommand.GrabFrameTexture( "PrevFrameTexture" );

        var PreviousGBuffer	     = Commands.GetRenderTarget( "PrevGBuffer",  ImageFormat.RGBA8888, sizeFactor: downsampleRatio );
        
        var Radiance0 = Commands.GetRenderTarget( $"Radiance{pingPong}", ImageFormat.RGBA16161616F, sizeFactor: downsampleRatio );
        var Radiance1 = Commands.GetRenderTarget( $"Radiance{!pingPong}", ImageFormat.RGBA16161616F, sizeFactor: downsampleRatio );

        var Variance0 = Commands.GetRenderTarget( $"Variance{pingPong}", ImageFormat.R16F, sizeFactor: downsampleRatio );
        var Variance1 = Commands.GetRenderTarget( $"Variance{!pingPong}", ImageFormat.R16F, sizeFactor: downsampleRatio );

        var SampleCount0 = Commands.GetRenderTarget( $"Sample Count{pingPong}", ImageFormat.R16F, sizeFactor: downsampleRatio );
        var SampleCount1 = Commands.GetRenderTarget( $"Sample Count{!pingPong}", ImageFormat.R16F, sizeFactor: downsampleRatio );

        var AverageRadiance0 = Commands.GetRenderTarget( $"Average Radiance{pingPong}", ImageFormat.RGBA8888, sizeFactor: 8 * downsampleRatio );
        var AverageRadiance1 = Commands.GetRenderTarget( $"Average Radiance{!pingPong}", ImageFormat.RGBA8888, sizeFactor: 8 * downsampleRatio );

        var ReprojectedRadiance	= Commands.GetRenderTarget( "Reprojected Radiance", ImageFormat.RGBA16161616F, sizeFactor: downsampleRatio );

        ComputeShader reflectionsCs = new ComputeShader("dynamic_reflections_cs");

        foreach( var pass in Enum.GetValues( typeof( Passes ) ) )
        {
            switch ( pass )
            {               
            // I'd like to use the ray dispatches from GPU Buffers , which would be faster and higher quality
            // but this is hard in the command list api without having per-viewport configuration
            // right now it's a direct reimplementation of C++ version but without Reflection MODE
            // case Passes.ClassifyTiles:
            //    {
            //        break;
            //    }
            case Passes.Intersect:
                Commands.Set( "OutRadiance", Radiance0.ColorTexture );
                break;

            case Passes.DenoiseReproject:
                Commands.Set( "SampleCountIntersection", SamplesPerPixel );
                Commands.Set( "AverageRadianceHistory", AverageRadiance1.ColorTexture );
                Commands.Set( "VarianceHistory", Variance1.ColorTexture ); 
                Commands.Set( "SampleCountHistory", SampleCount1.ColorTexture );

                Commands.Set( "OutReprojectedRadiance", ReprojectedRadiance.ColorTexture );
                Commands.Set( "OutAverageRadiance", AverageRadiance0.ColorTexture );
                Commands.Set( "OutVariance", Variance0.ColorTexture );
                Commands.Set( "OutSampleCount", SampleCount0.ColorTexture );
                break;

            case Passes.DenoisePrefilter:
                Commands.Set( "Radiance", Radiance0.ColorTexture );
                Commands.Set( "Variance", Variance0.ColorTexture );
                Commands.Set( "SampleCountHistory", SampleCount0.ColorTexture );

                Commands.Set( "OutRadiance", Radiance1.ColorTexture );
                Commands.Set( "OutVariance", Variance1.ColorTexture );
                Commands.Set( "OutSampleCount", SampleCount1.ColorTexture );
                break;

            case Passes.DenoiseResolveTemporal:
                Commands.Set( "Radiance", Radiance1.ColorTexture );
                Commands.Set( "ReprojectedRadiance", ReprojectedRadiance.ColorTexture );
                Commands.Set( "Variance", Variance1.ColorTexture );
                Commands.Set( "SampleCount", SampleCount1.ColorTexture );

                Commands.Set( "OutRadiance", Radiance0.ColorTexture );
                Commands.Set( "OutVariance", Variance0.ColorTexture );
                Commands.Set( "OutSampleCount", SampleCount0.ColorTexture );
                break;
            }

            // Common settings for all passes
            Commands.Set( "PreviousGBuffer", PreviousGBuffer.ColorTexture );
            Commands.Set( "PreviousFrameColor", PreviousFrameColorRT.ColorTexture );
            Commands.Set( "AverageRadiance", AverageRadiance0.ColorTexture );
            Commands.Set( "AverageRadianceHistory", AverageRadiance1.ColorTexture );

            // Set the pass
            Commands.SetCombo( "D_PASS", (int)pass );
            Commands.DispatchCompute( reflectionsCs, ReprojectedRadiance.Size );
            break;
        }

        // Final SSR color to be used by shaders
        Commands.SetGlobal( "ReflectionColorIndex", Radiance0.ColorIndex );
        
    }
}
