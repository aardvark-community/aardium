namespace Aardvark.SceneGraph

open Offler
open System
open Aardvark.Application
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.SceneGraph
open System.Threading

type BrowserTexture(texture : IAdaptiveResource<ITexture>, image : ref<PixImage<byte>>) =
    member x.Texture = texture
    member x.ReadPixel(pos : V2i) =
        image.Value.GetMatrix<C4b>().[pos]

module BrowserTexture =


    module private Align =
        
        let prev (align : V2i) (v : V2i) =
            let x = 
                let mx = v.X % align.X
                if mx = 0 then v.X
                else v.X - mx
            let y =
                let my = v.Y % align.Y
                if my = 0 then v.Y
                else v.Y - my
            V2i(x,y)

                                            
        let next (align : V2i) (v : V2i) =
            let x = 
                let mx = v.X % align.X
                if mx = 0 then v.X
                else v.X - mx + align.X
            let y =
                let my = v.Y % align.Y
                if my = 0 then v.Y
                else v.Y - my + align.Y
            V2i(x,y)

    module Vulkan =
        open Aardvark.Rendering.Vulkan
        open Microsoft.FSharp.NativeInterop

        let create (runtime : Runtime) (client : Offler) =
            let device = runtime.Device

            let emptySub = { new IDisposable with member x.Dispose() = () }
            let mutable tex = Unchecked.defaultof<Image>
            let mutable running = false
            let mutable dirty = false
            let mutable sub = emptySub

            
            let allIndices = device.PhysicalDevice.QueueFamilies |> Array.map (fun f -> uint32 f.index)


            let createImage (textureDim : V3i) =
                use pAll = fixed allIndices
                let fmt = VkFormat.R8g8b8a8Unorm
                use pInfo =
                    fixed [|
                        VkImageCreateInfo(
                            VkImageCreateFlags.None,
                            VkImageType.D2d, fmt,
                            VkExtent3D(textureDim.X, textureDim.Y, textureDim.Z),
                            1u, 1u, VkSampleCountFlags.D1Bit, VkImageTiling.Optimal, 
                            VkImageUsageFlags.TransferDstBit ||| VkImageUsageFlags.SampledBit,
                            VkSharingMode.Concurrent,
                            uint32 allIndices.Length,
                            pAll,
                            VkImageLayout.Preinitialized
                        )
                    |]

                let img = [| VkImage.Null |]
                use pImg = fixed img
                VkRaw.vkCreateImage(device.Handle, pInfo, NativePtr.zero, pImg)
                |> ignore
                let handle = img.[0]

                let reqs = [| VkMemoryRequirements() |]
                use pReqs = fixed reqs
                VkRaw.vkGetImageMemoryRequirements(device.Handle, handle, pReqs)
                let reqs = reqs.[0]

                let mem = device.DeviceMemory.AllocRaw(reqs.size)
                VkRaw.vkBindImageMemory(device.Handle, handle, mem.Handle, 0UL)
                |> ignore


                let img = new Image(device, handle, textureDim, 1, 1, 1, TextureDimension.Texture2D, fmt, mem, VkImageLayout.Preinitialized, VkImageLayout.General)
                device.perform{
                    do! Command.TransformLayout(img, VkImageLayout.General)
                }
                img



            let toDispose : ref<list<Image>> = ref [] //System.Collections.Generic.List<Image>()
            let resource = 
                { new AdaptiveResource<ITexture>() with
                    member x.Create() =
                        running <- true

                        let thread =
                            startThread <| fun () ->
                                while running do
                                    lock emptySub (fun () ->
                                        while not dirty do
                                            Monitor.Wait emptySub |> ignore
                                        dirty <- false
                                    )
                                    if running then transact (fun () -> x.MarkOutdated())



                        tex <- createImage (V3i(client.Width, client.Height, 1))
                   
                        sub <- 
                            client.Subscribe (fun img ->
                                try
                                    if img.totalWidth <> tex.Size.X || img.totalHeight <> tex.Size.Y then
                                        // recreate
                                        let size = V3i(img.totalWidth, img.totalHeight, 1)
                                        let newImg = createImage size
                                        
                                        let temp = device.CreateTensorImage<byte>(size, Col.Format.RGBA, false)
                                        temp.Write(img.data, nativeint (size.X * 4), Col.Format.BGRA, ImageTrafo.Identity)
                                        device.CopyEngine.Enqueue [
                                            CopyCommand.Copy(temp, newImg.[TextureAspect.Color, 0, 0])
                                            CopyCommand.Callback temp.Dispose
                                        ]
                                        let o = tex
                                        tex <- newImg
                                        toDispose.Value <- o :: toDispose.Value
                                    elif img.width = img.totalWidth && img.height = img.totalHeight then    
                                        // full update
                                        let size = V3i(img.totalWidth, img.totalHeight, 1)

                                        let temp = device.CreateTensorImage<byte>(size, Col.Format.RGBA, false)
                                        temp.Write(img.data, nativeint (size.X * 4), Col.Format.BGRA, ImageTrafo.Identity)
                                        device.CopyEngine.Enqueue [
                                            CopyCommand.Copy(temp, tex.[TextureAspect.Color, 0, 0])
                                            CopyCommand.Callback temp.Dispose
                                        ]
                                    else
                                        // partial update
                                        let size = V3i(img.width, img.height, 1)
                                        let gran = device.TransferFamily.Info.minImgTransferGranularity.XY

                                        let cMin = V2i(img.x, img.y)
                                        let copyOffset = cMin |> Align.prev gran
                                        let copySize = cMin + size.XY - copyOffset |> Align.next gran

                                        let temp = device.CreateTensorImage<byte>(V3i(copySize, 1), Col.Format.RGBA, false)

                                        NativeVolume.using client.LastImage.Volume (fun src ->
                                            let srcPart = src.SubVolume(V3i(copyOffset, 0), V3i(copySize, 4))
                                            temp.Write(Col.Format.BGRA, srcPart)
                                        )

                                        device.CopyEngine.Enqueue [
                                            CopyCommand.Copy(temp, tex.[TextureAspect.Color, 0, 0], V3i(copyOffset, 0), V3i(copySize, 1))
                                            CopyCommand.Callback temp.Dispose
                                        ]
                                with e ->
                                    Log.error "%A" e
                                if not x.OutOfDate then
                                    lock emptySub (fun () ->
                                        dirty <- true
                                        Monitor.PulseAll emptySub
                                    )
                                    //System.Threading.Tasks.Task.Factory.StartNew(fun () -> transact (fun () -> x.MarkOutdated())) |> ignore
                            )

                    member x.Destroy() =
                        if running then
                            running <- false
                            lock emptySub (fun () ->
                                dirty <- true
                                Monitor.PulseAll emptySub
                            )
                            sub.Dispose()
                            sub <- emptySub
                            tex.Dispose()
                            tex <- Unchecked.defaultof<_>

                    member x.Compute(at, rt) =
                        let old = Interlocked.Exchange(toDispose, [])
                        for img in old do img.Dispose()
                        tex :> ITexture
                } :> IAdaptiveResource<_>

            resource
      
    module GL =
        open Microsoft.FSharp.NativeInterop
        open System.Runtime.InteropServices
        open OpenTK.Graphics
        open OpenTK.Graphics.OpenGL4
        open Aardvark.Rendering.GL

        let create (runtime : Runtime) (client : Offler) =
            let ctx = runtime.Context
            let emptySub = { new IDisposable with member x.Dispose() = () }
            let mutable tex = Unchecked.defaultof<Texture>
            let mutable running = false
            let mutable dirty = false
            let mutable sub = emptySub
            let mutable pbo = 0

            let resource = 
                { new AdaptiveResource<ITexture>() with
                    member x.Create() =
                        running <- true

                        let check name =    
                            let err = GL.GetError()
                            if err <> ErrorCode.NoError then Log.warn "%s: %A" name err

                        let createPBO(size : int) =
                            let p = GL.GenBuffer()
                            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, p)
                            check "BindBuffer"
                            GL.BufferStorage(BufferTarget.PixelUnpackBuffer, nativeint size, 0n, BufferStorageFlags.MapWriteBit)
                            check "BufferStorage"
                            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0)
                            check "BindBuffer"
                            p

                        let mapPBO (buffer : int) (size : int) (action : nativeint -> unit) =
                            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, buffer)
                            check "BindBuffer"
                            //let ptr = GL.MapNamedBufferRange(buffer, 0n, nativeint size, BufferAccessMask.MapInvalidateRangeBitExt ||| BufferAccessMask.MapWriteBit)
                            let ptr = GL.MapBufferRange(BufferTarget.PixelUnpackBuffer, 0n, nativeint size, BufferAccessMask.MapInvalidateBufferBit ||| BufferAccessMask.MapWriteBit)
                            check "MapBufferRange"
                            try action ptr
                            finally 
                            
                                GL.UnmapBuffer(BufferTarget.PixelUnpackBuffer) |> ignore
                                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0)


                        let thread =
                            startThread <| fun () ->
                                while running do
                                    lock emptySub (fun () ->
                                        while not dirty do
                                            Monitor.Wait emptySub |> ignore
                                        dirty <- false
                                    )
                                    if running then transact (fun () -> x.MarkOutdated())

                        //using ctx.ResourceLock (fun _ ->
                        //    tex <- ctx.CreateTexture2D(V2i(client.Width, client.Height), 1, TextureFormat.Rgba8, 1)
                        //    pbo <- createPBO (4 * client.Width * client.Height)
                        //)

                        sub <- 
                            client.Subscribe (fun img ->
                                use __ = ctx.ResourceLock
                    
                                let size = V2i(img.width, img.height)
                                let bytes = 4 * size.X * size.Y
                            
                                if pbo = 0 || img.totalWidth <> tex.Size.X || img.totalHeight <> tex.Size.Y then
                                    // recreate
                                    let newImg = ctx.CreateTexture2D(size, 1, TextureFormat.Rgba8, 1)
                                    let newPBO = createPBO bytes
                                
                                    mapPBO newPBO bytes (fun ptr -> Marshal.Copy(img.data, ptr, bytes))

                                    GL.BindBuffer(BufferTarget.PixelUnpackBuffer, newPBO)
                                    GL.BindTexture(TextureTarget.Texture2D, newImg.Handle)
                                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, size.X, size.Y, PixelFormat.Bgra, PixelType.UnsignedByte, 0n)
                                    GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0)
                                    GL.BindTexture(TextureTarget.Texture2D, 0)
                              
                                    if pbo <> 0 then
                                        ctx.Delete tex
                                        GL.UnmapNamedBuffer pbo |> ignore
                                        GL.DeleteBuffer pbo
                                    tex <- newImg
                                    pbo <- newPBO

                                elif img.width = img.totalWidth && img.height = img.totalHeight then  
                                    // full update
                                    mapPBO pbo bytes (fun ptr -> Marshal.Copy(img.data, ptr, bytes))

                                    GL.BindBuffer(BufferTarget.PixelUnpackBuffer, pbo)
                                    GL.BindTexture(TextureTarget.Texture2D, tex.Handle)
                                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, size.X, size.Y, PixelFormat.Bgra, PixelType.UnsignedByte, 0n)
                                    GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0)
                                    GL.BindTexture(TextureTarget.Texture2D, 0)

                                else
                                    // partial update
                                    mapPBO pbo bytes (fun ptr -> Marshal.Copy(img.data, ptr, bytes))

                                    GL.BindBuffer(BufferTarget.PixelUnpackBuffer, pbo)
                                    GL.BindTexture(TextureTarget.Texture2D, tex.Handle)
                                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, img.x, img.y, size.X, size.Y, PixelFormat.Bgra, PixelType.UnsignedByte, 0n)
                                    GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0)
                                    GL.BindTexture(TextureTarget.Texture2D, 0)

                                if not x.OutOfDate then
                                    lock emptySub (fun () ->
                                        dirty <- true
                                        Monitor.PulseAll emptySub
                                    )
                                    //System.Threading.Tasks.Task.Factory.StartNew(fun () -> transact (fun () -> x.MarkOutdated())) |> ignore
                            )
                    member x.Destroy() =
                        if running then
                            running <- false
                            lock emptySub (fun () ->
                                dirty <- true
                                Monitor.PulseAll emptySub
                            )
                            sub.Dispose()
                            sub <- emptySub
                            if pbo <> 0 then
                                use __ = ctx.ResourceLock
                                ctx.Delete tex
                                GL.DeleteBuffer pbo
                                tex <- Unchecked.defaultof<_>
                    member x.Compute(at, rt) =
                        tex :> ITexture
                } :> IAdaptiveResource<_>

            resource

    let create (runtime : IRuntime) (client : Offler) =
        match runtime with
        | :? Aardvark.Rendering.Vulkan.Runtime as r -> Vulkan.create r client
        | :? Aardvark.Rendering.GL.Runtime as r -> GL.create r client
        | _ -> failwithf "unexpected runtime: %A" runtime

type BrowserRenderInfo =
    {
        browser     : Offler
        keyboard    : option<IKeyboard>
        mouse       : option<IMouse>
        focus       : cval<bool>
    }

module Sg =
    open Aardvark.Rendering
    open Aardvark.SceneGraph
    open Aardvark.SceneGraph.Semantics

    type BrowserNode(info : BrowserRenderInfo) =
        interface ISg
        member x.Info = info

    let browser (info : BrowserRenderInfo) =
        BrowserNode(info) :> ISg

    module private BrowserIO =
        let ofList (d : list<IDisposable>) =
            { new IDisposable with
                member x.Dispose() =
                    for e in d do e.Dispose()
            }

        let tryGetPixel  (mvp : aval<Trafo3d>) (browser : Offler) (p : PixelPosition) =
            let mvp = AVal.force mvp
            let tc = p.NormalizedPosition
            let ndc = V2d(2.0 * tc.X - 1.0, 1.0 - 2.0 * tc.Y)

            let n = mvp.Backward.TransformPosProj(V3d(ndc, -1.0))
            let f = mvp.Backward.TransformPosProj(V3d(ndc, 1.0))

            let ray = Ray3d(n, Vec.normalize (f - n))

            let mutable t = 0.0
            if ray.Intersects(Plane3d.ZPlane, &t) && t >= 0.0 then
                let localPt = ray.GetPointOnRay(t).XY
                if localPt.AllGreaterOrEqual -1.0 && localPt.AllSmallerOrEqual 1.0 then
                    let tc = 0.5 * localPt + V2d.Half
                    let fpx = tc * V2d(browser.Width, browser.Height)
                    let px = round fpx |> V2i
                    Some px
                else
                    None
            else
                None

        let installMouse (focus : cval<bool>) (mvp : aval<Trafo3d>) (browser : Offler) (keyboard : option<IKeyboard>) (mouse : IMouse) =
            
            let ctrl = match keyboard with | Some k -> AVal.logicalOr [k.IsDown Keys.LeftCtrl; k.IsDown Keys.RightCtrl] | None -> AVal.constant false
            let shift = match keyboard with | Some k -> AVal.logicalOr [k.IsDown Keys.LeftShift; k.IsDown Keys.RightShift] | None -> AVal.constant false
            let alt = match keyboard with | Some k -> AVal.logicalOr [k.IsDown Keys.LeftAlt; k.IsDown Keys.RightAlt] | None -> AVal.constant false
            
            let mutable inside = false
            let mutable lastPixel = V2i.Zero
            let anyButton = [MouseButtons.Left; MouseButtons.Middle; MouseButtons.Right] |> List.existsA (fun b -> mouse.IsDown b)
            ofList [
                //focus.AddCallback (function 
                //    | false -> 
                //        inside <- false
                //        browser.SetFocus false
                //    | true -> 
                //        browser.SetFocus true
                //)
                mouse.Move.Values.Subscribe (fun (_, p) ->
                    if not (AVal.force anyButton) || AVal.force focus then
                        match tryGetPixel mvp browser p with
                        | Some p -> 
                            if not inside then browser.MouseEnter(p.X, p.Y, AVal.force ctrl, AVal.force alt, AVal.force shift)
                            inside <- true
                            lastPixel <- p
                            browser.MouseMove(p.X, p.Y, AVal.force ctrl, AVal.force alt, AVal.force shift)
                        | None ->
                            if inside then browser.MouseLeave(lastPixel.X, lastPixel.Y, AVal.force ctrl, AVal.force alt, AVal.force shift)
                            inside <- false
                )
                mouse.Down.Values.Subscribe (fun b ->
                    let p = AVal.force mouse.Position
                    match tryGetPixel mvp browser p with
                    | Some p ->
                        let c = browser.ReadPixel(p)
                        if true || c.A >= 127uy then
                            transact (fun () -> focus.Value <- true)
                            let button =
                                match b with
                                | MouseButtons.Left -> MouseButton.Left
                                | MouseButtons.Right -> MouseButton.Right
                                | _ -> MouseButton.Middle
                            browser.MouseDown(p.X, p.Y, button, AVal.force ctrl, AVal.force alt, AVal.force shift)
                        else
                            () //transact (fun () -> focus.Value <- false)
                    | None ->
                        //transact (fun () -> focus.Value <- false)
                        ()
                )
    
                mouse.Up.Values.Subscribe (fun b ->
                    if AVal.force focus then
                        let p = AVal.force mouse.Position
                        match tryGetPixel mvp browser p with
                        | Some p ->
                            let button =
                                match b with
                                | MouseButtons.Left -> MouseButton.Left
                                | MouseButtons.Right -> MouseButton.Right
                                | _ -> MouseButton.Middle
                            browser.MouseUp(p.X, p.Y, button, AVal.force ctrl, AVal.force alt, AVal.force shift)
                        | None ->
                            ()
                )
    
                mouse.Click.Values.Subscribe (fun b ->
                    let p = AVal.force mouse.Position
                    match tryGetPixel mvp browser p with
                    | Some p ->
                        let button =
                            match b with
                            | MouseButtons.Left -> MouseButton.Left
                            | MouseButtons.Right -> MouseButton.Right
                            | _ -> MouseButton.Middle
                        browser.MouseClick(p.X, p.Y, button, 1, AVal.force ctrl, AVal.force alt, AVal.force shift)
                    | None ->
                        ()
                )
                mouse.DoubleClick.Values.Subscribe (fun b ->
                    let p = AVal.force mouse.Position
                    match tryGetPixel mvp browser p with
                    | Some p ->
                        let button =
                            match b with
                            | MouseButtons.Left -> MouseButton.Left
                            | MouseButtons.Right -> MouseButton.Right
                            | _ -> MouseButton.Middle
                        browser.MouseClick(p.X, p.Y, button, 2, AVal.force ctrl, AVal.force alt, AVal.force shift)
                    | None ->
                        ()
                )

                mouse.Scroll.Values.Subscribe(fun d ->
                    if AVal.force focus then
                        let p = AVal.force mouse.Position
                        match tryGetPixel mvp browser p with
                        | Some p ->
                            browser.MouseWheel(p.X, p.Y, 0.0, d, AVal.force ctrl, AVal.force alt, AVal.force shift)
                        | None ->
                            ()
                )
            ]

        let translateKey (keys : Keys) =
            match keys with
            | Keys.D0 -> "0"
            | Keys.D1 -> "1"
            | Keys.D2 -> "2"
            | Keys.D3 -> "3"
            | Keys.D4 -> "4"
            | Keys.D5 -> "5"
            | Keys.D6 -> "6"
            | Keys.D7 -> "7"
            | Keys.D8 -> "8"
            | Keys.D9 -> "9"
            
            | Keys.NumPad0 -> "num0"
            | Keys.NumPad1 -> "num1"
            | Keys.NumPad2 -> "num2"
            | Keys.NumPad3 -> "num3"
            | Keys.NumPad4 -> "num4"
            | Keys.NumPad5 -> "num5"
            | Keys.NumPad6 -> "num6"
            | Keys.NumPad7 -> "num7"
            | Keys.NumPad8 -> "num8"
            | Keys.NumPad9 -> "num9"
            
            | Keys.A -> "A"
            | Keys.B -> "B"
            | Keys.C -> "C"
            | Keys.D -> "D"
            | Keys.E -> "E"
            | Keys.F -> "F"
            | Keys.G -> "G"
            | Keys.H -> "H"
            | Keys.I -> "I"
            | Keys.J -> "J"
            | Keys.K -> "K"
            | Keys.L -> "L"
            | Keys.M -> "M"
            | Keys.N -> "N"
            | Keys.O -> "O"
            | Keys.P -> "P"
            | Keys.Q -> "Q"
            | Keys.R -> "R"
            | Keys.S -> "S"
            | Keys.T -> "T"
            | Keys.U -> "U"
            | Keys.V -> "V"
            | Keys.W -> "W"
            | Keys.X -> "X"
            | Keys.Y -> "Y"
            | Keys.Z -> "Z"
            
            | Keys.F1 -> "F1"
            | Keys.F2 -> "F2"
            | Keys.F3 -> "F3"
            | Keys.F4 -> "F4"
            | Keys.F5 -> "F5"
            | Keys.F6 -> "F6"
            | Keys.F7 -> "F7"
            | Keys.F8 -> "F8"
            | Keys.F9 -> "F9"
            | Keys.F10 -> "F10"
            | Keys.F11 -> "F11"
            | Keys.F12 -> "F12"
            | Keys.F13 -> "F13"
            | Keys.F14 -> "F14"
            | Keys.F15 -> "F15"
            | Keys.F16 -> "F16"
            | Keys.F17 -> "F17"
            | Keys.F18 -> "F18"
            | Keys.F19 -> "F19"
            | Keys.F20 -> "F20"
            | Keys.F21 -> "F21"
            | Keys.F22 -> "F22"
            | Keys.F23 -> "F23"
            | Keys.F24 -> "F24"
            
            | Keys.Space -> "Space"
            | Keys.Tab -> "Tab"
            | Keys.Scroll -> "Scrolllock"
            | Keys.CapsLock -> "Capslock"
            | Keys.NumLock -> "Numlock"
            | Keys.Back -> "Backspace"
            | Keys.Insert -> "Inser"
            | Keys.Delete -> "Delete"
            | Keys.Return -> "Enter"
            | Keys.Home -> "Home"
            | Keys.End -> "End"
            | Keys.PageUp -> "PageUp"
            | Keys.PageDown -> "PageDown"
            | Keys.Escape -> "Escape"
            | Keys.Print -> "PrintScreen"
            
            | Keys.Up -> "Up"
            | Keys.Down -> "Down"
            | Keys.Left -> "Left"
            | Keys.Right -> "Right"

            | Keys.LeftCtrl | Keys.RightCtrl
            | Keys.LeftAlt | Keys.RightAlt 
            | Keys.LWin | Keys.RWin
            | Keys.LeftShift | Keys.RightShift ->
                ""

            | _ -> 
                Log.warn "unknown key: %A" keys
                ""

        let installKeyboard (focus : cval<bool>) (browser : Offler) (keyboard : IKeyboard) =
            let ctrl = AVal.logicalOr [keyboard.IsDown Keys.LeftCtrl; keyboard.IsDown Keys.RightCtrl] 
            let shift = AVal.logicalOr [keyboard.IsDown Keys.LeftShift; keyboard.IsDown Keys.RightShift]
            let super = AVal.logicalOr [keyboard.IsDown Keys.LWin; keyboard.IsDown Keys.RWin]
            let alt = keyboard.IsDown Keys.LeftAlt
            let altGr = keyboard.IsDown Keys.RightAlt
            
            let withModifiers (code : string) =
                let mutable code = code
                if AVal.force alt then code <- "Alt+" + code 
                if AVal.force altGr then code <- "AltGr+" + code 
                if AVal.force shift then code <- "Shift+" + code 
                if AVal.force ctrl then code <- "Ctrl+" + code 
                if AVal.force super then code <- "Super+" + code 
                code

            ofList [
                keyboard.DownWithRepeats.Values.Subscribe(fun k ->
                    if AVal.force focus then
                        let code = translateKey k
                        if code <> "" then 
                            let full = withModifiers code
                            if full = "Enter" then
                                browser.Input "\u000d"
                            else
                                browser.KeyDown full
                )

                keyboard.Up.Values.Subscribe(fun k ->
                    if AVal.force focus then
                        let code = translateKey k
                        if code <> "" then browser.KeyUp(withModifiers code)
                )

                keyboard.Press.Values.Subscribe(fun c ->
                    if AVal.force focus then
                        browser.Input(System.String(c, 1))
                )
            ]

    [<Rule>]
    type BrowserNodeSem() =

        member x.RenderObjects(node : BrowserNode, scope : Ag.Scope) : aset<IRenderObject> =
            let runtime = scope.Runtime
            let o = RenderObject.ofScope scope
            
            let m = scope.ModelTrafo
            let v = scope.ViewTrafo
            let p = scope.ProjTrafo
            let mvp = (m,v,p) |||> AVal.map3 (fun m v p -> m * v * p)

            let tex = BrowserTexture.create runtime node.Info.browser

            let activate () =
                let mouse = 
                    match node.Info.mouse with
                    | Some mouse ->
                        BrowserIO.installMouse node.Info.focus mvp node.Info.browser node.Info.keyboard mouse
                    | None ->
                        { new IDisposable with member x.Dispose() = () }

                let keyboard = 
                    match node.Info.keyboard with
                    | Some keyboard ->
                        BrowserIO.installKeyboard node.Info.focus node.Info.browser keyboard
                    | None ->
                        { new IDisposable with member x.Dispose() = () }
                        
                { new IDisposable with 
                    member x.Dispose() =
                        mouse.Dispose()
                        keyboard.Dispose()
                        transact (fun () -> node.Info.focus.Value <- false)
                }

            let special =
                UniformProvider.ofList [
                    DefaultSemantic.DiffuseColorTexture, tex :> IAdaptiveValue
                    //Symbol.Create "ModelTrafo", model :> IAdaptiveValue
                ]


            o.Uniforms <-
                UniformProvider.union special o.Uniforms

            o.VertexAttributes <- 
                AttributeProvider.ofList [
                    DefaultSemantic.Positions, [| V3f.NNO; V3f.PNO; V3f.IIO; V3f.NPO |] :> Array
                    DefaultSemantic.DiffuseColorCoordinates, [| V2f.OO; V2f.IO; V2f.II; V2f.OI |] :> Array
                ]
            o.Indices <-
                BufferView(AVal.constant (ArrayBuffer [|0;1;2; 0;2;3|] :> IBuffer), typeof<int>) |> Some

            o.DrawCalls <- DrawCalls.Direct (AVal.constant [DrawCallInfo(6)])

            o.Activate <- activate
            ASet.single (o :> IRenderObject)

        member x.LocalBoundingBox(node : BrowserNode, scope : Ag.Scope) =
            AVal.constant (Box3d(V3d(-1.0, -1.0, -Constant.PositiveTinyValue), V3d(1.0, 1.0, Constant.PositiveTinyValue)))
            
        member x.GlobalBoundingBox(node : BrowserNode, scope : Ag.Scope) =
            let b = Box3d(V3d(-1.0, -1.0, -Constant.PositiveTinyValue), V3d(1.0, 1.0, Constant.PositiveTinyValue))
            scope.ModelTrafo |> AVal.map (fun t -> b.Transformed t)

