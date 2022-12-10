module RenderingTest
open System
open System.Runtime.InteropServices
open System.IO

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Semantics

open Aardvark.Application
open Aardvark.Application.Slim
open Aardvark.Rendering.GL

open Offler
open Aardium


module Shader =
    open FShade

    let browserSampler =
        sampler2d {
            texture uniform?DiffuseColorTexture
            filter Filter.MinMagPoint
            addressU WrapMode.Clamp
            addressV WrapMode.Clamp
        }

    let fullScreen (v : Effects.Vertex) =
        fragment {
            let coord = V2d(0.5 + 0.5 * v.pos.X, 0.5 - 0.5 * v.pos.Y)
            let pixel = V2d uniform.ViewportSize * coord |> V2i
            let textureSize = browserSampler.Size

            if pixel.X < textureSize.X && pixel.Y < textureSize.Y then
                let color = browserSampler.[pixel]
                return color
            else
                return V4d(0.0,0.0,0.0,0.0)
        }


let run() =
    // local aardium
    let distPath = 
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "Aardium", "dist")
    let distName =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then "Aardium-win32-x64"
        elif RuntimeInformation.IsOSPlatform OSPlatform.Linux then "Aardium-linux-x64"
        elif RuntimeInformation.IsOSPlatform OSPlatform.OSX then "Aardium-darwin-x64"
        else failwith "bad platform"

    let exe = Path.Combine(distPath, distName)
    //Aardium.initPath exe
    Aardium.init()
    Aardvark.Init()

    use app = new OpenGlApplication()
    let win = app.CreateGameWindow()

    use offler =
        let s = AVal.force win.Sizes
        Offler.Logger <- fun b s -> printfn "%b -> %s" b s
        new Offler {
            url = "https://developer.mozilla.org/en-US/docs/Web/API/Element/setPointerCapture"
            width = s.X
            height = s.Y
            incremental = true
        }
    offler.OpenDevTools()

    let browserFocus = cval false

    let sg =
        Sg.browser {
            browser = offler
            mouse = Some win.Mouse
            keyboard = Some win.Keyboard
            focus = browserFocus
        }
        |> Sg.shader {
            do! Shader.fullScreen |> toEffect
        }


    let task =
        RenderTask.ofList [
            app.Runtime.CompileClear(win.FramebufferSignature, AVal.constant C4f.Gray)
            app.Runtime.CompileRender(win.FramebufferSignature, sg)
        ]
    
    win.RenderTask <- task
    win.Run()

    0