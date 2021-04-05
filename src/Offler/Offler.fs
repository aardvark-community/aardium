namespace Offler

open Microsoft.FSharp.NativeInterop
open System
open System.Threading
open System.Text
open System.Diagnostics
open System.Net
open System.Net.WebSockets
open System.Runtime.InteropServices
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open Aardvark.Base
open Aardium

#nowarn "9"

type MouseButton =
    | Left
    | Middle
    | Right
    
[<Struct>]
type OfflerImageInfo =
    {
        x : int
        y : int
        width : int
        height : int
        totalWidth : int
        totalHeight : int
        data : nativeint
        byteLength : int
    }
    member x.isFullFrame = x.width = x.totalWidth && x.height = x.totalHeight

[<AutoOpen>]
module private ClientHelpers =

    type WebSocket with
        member x.ReceiveMessage() : Async<byte[]> =
            async {
                let mutable result = [||]
                let mutable fin = false
                let buffer = ArraySegment (Array.zeroCreate 65536)
                let! ct = Async.CancellationToken
                while not fin do
                    let! r = x.ReceiveAsync(buffer, ct) |> Async.AwaitTask
                    if r.EndOfMessage then
                        result <- Array.append result (Array.take r.Count buffer.Array)
                        fin <- true
                return result
            }
            
        member x.ReceiveString() : Async<string> =
            async {
                let! data = x.ReceiveMessage()
                return Encoding.UTF8.GetString(data)
            }

        member x.Send(data : string) =
            async {
                let! ct = Async.CancellationToken
                let arr = Encoding.UTF8.GetBytes data
                do! x.SendAsync(ArraySegment arr, WebSocketMessageType.Text, true, ct) |> Async.AwaitTask
            }

    type MouseEvent =
        {
            x : int
            y : int
            shift : bool
            alt : bool
            ctrl : bool
        }

    type Command =
        | Init of url : string * width : int * height : int * mapName : string * mapSize : int
        | Resize of width : int * height : int
        | MouseMove of MouseEvent
        | MouseEnter of MouseEvent
        | MouseLeave of MouseEvent
        | MouseDown of MouseEvent * button : MouseButton * clickCount : int
        | MouseUp of MouseEvent * button : MouseButton
        | MouseWheel of MouseEvent * deltaX : float * deltaY : float
        | ContextMenu of MouseEvent * button : MouseButton * clickCount : int
        | KeyDown of code : string
        | KeyUp of code : string
        | Input of code : string
        | SetFocus of focus : bool
        | Custom of id : option<string> * js : string
        | Navigate of url : string
        | OpenDevTools
        | RequestFullFrame

    type Message =
        | FullFrame of width : int * height : int * offset : int * byteLength : int
        | PartialFrame of width : int * height : int * dirtyX : int * dirtyY : int * dirtyWidth : int * dirtyHeight : int * offset : int * byteLength : int
        | InitComplete
        | ChangeCursor of name : string
        | Result of id : string * value : JToken
        | Unknown of string


    module Message =
        let unpickle (str : string) =
            try
                let msg  = JObject.Parse(str)
                match msg.GetValue "type" |> string with
                | "fullframe" ->
                    let width : int = msg.GetValue("width") |> JToken.op_Explicit
                    let height : int = msg.GetValue("height") |> JToken.op_Explicit
                    let offset : int = msg.GetValue("offset") |> JToken.op_Explicit
                    let byteLength : int = msg.GetValue("byteLength") |> JToken.op_Explicit
                    FullFrame(width, height, offset, byteLength)
                | "partialframe" ->
                    let width : int = msg.GetValue("width") |> JToken.op_Explicit
                    let height : int = msg.GetValue("height") |> JToken.op_Explicit
                    let offset : int = msg.GetValue("offset") |> JToken.op_Explicit
                    let byteLength : int = msg.GetValue("byteLength") |> JToken.op_Explicit
                    let dx : int = msg.GetValue("dx") |> JToken.op_Explicit
                    let dy : int = msg.GetValue("dy") |> JToken.op_Explicit
                    let dw : int = msg.GetValue("dw") |> JToken.op_Explicit
                    let dh : int = msg.GetValue("dh") |> JToken.op_Explicit
                    PartialFrame(width, height, dx, dy, dw, dh, offset, byteLength)
                    
                | "initComplete" ->
                    InitComplete

                | "changecursor" ->
                    let name : string = msg.GetValue("name") |> JToken.op_Explicit
                    ChangeCursor name
                | "result" ->
                    let id : string = msg.GetValue("id") |> JToken.op_Explicit
                    let result = msg.GetValue("result")
                    Result(id, result)
                    
                | other ->
                    Unknown str
            with e ->
                Unknown str

    module Command =
        let private pickeMouseButton (b : MouseButton) =
            match b with
            | Left -> "left"
            | Middle -> "middle"
            | Right -> "right"

        let pickle (cmd : Command) =
            let o = JObject()

            let setMouse (o : JObject) (m : MouseEvent) =
                let arr =
                    [|
                        if m.shift then "shift" :> obj
                        if m.ctrl then "ctrl" :> obj
                        if m.alt then "alt" :> obj
                    |]
                o.["x"] <- JToken.op_Implicit m.x
                o.["y"] <- JToken.op_Implicit m.y
                o.["modifers"] <- JArray(arr)
                

            match cmd with
            | Init(url, width, height, mapName, mapSize) ->
                o.["command"] <- JToken.op_Implicit "init"
                o.["url"] <- JToken.op_Implicit url
                o.["width"] <- JToken.op_Implicit width
                o.["height"] <- JToken.op_Implicit height
                o.["mapName"] <- JToken.op_Implicit mapName
                o.["mapSize"] <- JToken.op_Implicit mapSize

            | Navigate url ->
                o.["command"] <- JToken.op_Implicit "navigate"
                o.["url"] <- JToken.op_Implicit url
                

            | Resize(width, height) ->
                o.["command"] <- JToken.op_Implicit "resize"
                o.["width"] <- JToken.op_Implicit width
                o.["height"] <- JToken.op_Implicit height

            | RequestFullFrame ->
                o.["command"] <- JToken.op_Implicit "requestfullframe"
            | OpenDevTools ->
                o.["command"] <- JToken.op_Implicit "opendevtools"
                
            | SetFocus f ->
                o.["command"] <- JToken.op_Implicit "setfocus"
                o.["focus"] <- JToken.op_Implicit f

            | Custom(id, js) ->
                o.["command"] <- JToken.op_Implicit "custom"
                o.["js"] <- JToken.op_Implicit js
                match id with
                | Some id -> o.["id"] <- JToken.op_Implicit id
                | None -> ()

            | MouseMove m ->
                let evt = JObject()
                o.["command"] <- JToken.op_Implicit "inputevent"
                evt.["type"] <- JToken.op_Implicit "mouseMove"
                setMouse evt m
                o.["event"] <- evt
                
            | MouseEnter m ->
                let evt = JObject()
                o.["command"] <- JToken.op_Implicit "inputevent"
                evt.["type"] <- JToken.op_Implicit "mouseEnter"
                setMouse evt m
                o.["event"] <- evt
                
            | MouseLeave m ->
                let evt = JObject()
                o.["command"] <- JToken.op_Implicit "inputevent"
                evt.["type"] <- JToken.op_Implicit "mouseLeave"
                setMouse evt m
                o.["event"] <- evt

            | MouseDown(m,b,cc) ->
                let evt = JObject()
                o.["command"] <- JToken.op_Implicit "inputevent"
                evt.["type"] <- JToken.op_Implicit "mouseDown"
                setMouse evt m
                evt.["button"] <- JToken.op_Implicit (pickeMouseButton b)
                if cc > 0 then evt.["clickCount"] <- JToken.op_Implicit cc
                o.["event"] <- evt
            | ContextMenu(m,b,cc) ->
                let evt = JObject()
                o.["command"] <- JToken.op_Implicit "inputevent"
                evt.["type"] <- JToken.op_Implicit "contextMenu"
                setMouse evt m
                evt.["button"] <- JToken.op_Implicit (pickeMouseButton b)
                if cc > 0 then evt.["clickCount"] <- JToken.op_Implicit cc
                o.["event"] <- evt
            | MouseUp(m,b) ->
                let evt = JObject()
                o.["command"] <- JToken.op_Implicit "inputevent"
                evt.["type"] <- JToken.op_Implicit "mouseUp"
                setMouse evt m
                evt.["button"] <- JToken.op_Implicit (pickeMouseButton b)
                evt.["clickCount"] <- JToken.op_Implicit 1
                o.["event"] <- evt
            | MouseWheel(m, dx,dy) ->
                let evt = JObject()
                o.["command"] <- JToken.op_Implicit "inputevent"
                evt.["type"] <- JToken.op_Implicit "mouseWheel"
                setMouse evt m
                evt.["deltaX"] <- JToken.op_Implicit dx
                evt.["deltaY"] <- JToken.op_Implicit dy
                evt.["hasPreciseScrollingDeltas"] <- JToken.op_Implicit true
                o.["event"] <- evt
            | KeyDown(code) ->
                let evt = JObject()
                o.["command"] <- JToken.op_Implicit "inputevent"
                evt.["type"] <- JToken.op_Implicit "keyDown"
                evt.["keyCode"] <- JToken.op_Implicit code
                o.["event"] <- evt
            | KeyUp(code) ->
                let evt = JObject()
                o.["command"] <- JToken.op_Implicit "inputevent"
                evt.["type"] <- JToken.op_Implicit "keyUp"
                evt.["keyCode"] <- JToken.op_Implicit code
                o.["event"] <- evt
            | Input(code) ->
                let evt = JObject()
                o.["command"] <- JToken.op_Implicit "inputevent"
                evt.["type"] <- JToken.op_Implicit "char"
                evt.["keyCode"] <- JToken.op_Implicit code
                o.["event"] <- evt
                
            o.ToString(Formatting.None)

type OfflerInfo =
    {
        width   : int
        height  : int
        url     : string
    }


type Offler internal(ws : WebSocket, shared : ISharedMemory, url : string, width : int, height : int) =
    
    static let mutable logger : bool -> string -> unit = fun _ _ -> ()

    static let serverLock = obj()
    static let mutable server = Unchecked.defaultof<AardiumOffscreenServer>
    static let mutable serverRefCount = 0

    static let getServer() =
        lock serverLock (fun () ->
            if serverRefCount = 0 then
                let newServer =
                    Aardium.startOffscreenServer 0 <| fun isError message -> 
                        logger isError message
                server <- newServer
                serverRefCount <- 1
                newServer
            else
                serverRefCount <- serverRefCount + 1
                server
        )

    static let releaseServer() =
        lock serverLock (fun () ->
            if serverRefCount = 1 then
                logger false "stopping server"
                serverRefCount <- 0
                server.Stop()
                server <- Unchecked.defaultof<_>
            else
                serverRefCount <- serverRefCount - 1
        )
    

    let mutable isDisposed = false
    let cancel = new CancellationTokenSource()

    let mutable width = width
    let mutable height = height
    let mutable url = url

    let images = Event<OfflerImageInfo>()
    let mutable cursorName = "default"
    let cursorChanged = Event<string>()
    let resize = Event<int * int>()
    let mutable lastImage = PixImage<byte>(Col.Format.BGRA, V2i.II)

    let replaceImage (width : int) (height : int) (data : nativeint) =
        if lastImage.Size.X <> width || lastImage.Size.Y <> height then
            lastImage <- PixImage<byte>(Col.Format.BGRA, V2i(width, height))

        NativeVolume.using lastImage.Volume (fun pDst ->
            let pSrc = NativeVolume<byte>(NativePtr.ofNativeInt data, VolumeInfo(0L, V3l(width, height, 4), V3l(4, 4 * width, 1)))
            NativeVolume.copy pSrc pDst
        )

    let updateImage (x : int) (y : int) (width : int) (height : int) (data : nativeint) =
        NativeVolume.using lastImage.Volume (fun pFull ->
            let pDst = pFull.SubVolume(V3l(x, y, 0), V3l(width, height, 4))
            let pSrc = NativeVolume<byte>(NativePtr.ofNativeInt data, VolumeInfo(0L, V3l(width, height, 4), V3l(4, 4 * width, 1)))
            NativeVolume.copy pSrc pDst
        )

    let waiters = System.Collections.Concurrent.ConcurrentDictionary<string, JToken -> unit>()

    let waitFor (id : string) =
        let tcs = System.Threading.Tasks.TaskCompletionSource<JToken>()
        waiters.TryAdd(id, tcs.SetResult) |> ignore
        tcs.Task |> Async.AwaitTask

    let unknownMessages = Event<string>()

    let receiver =
        Async.StartAsTask(
            async {
                while not isDisposed do
                    let! msg = ws.ReceiveString()
                    if not isDisposed then
                        do! Async.SwitchToThreadPool()

                        match Message.unpickle msg with
                        | InitComplete ->
                            printfn "unexpected initialization"

                        | FullFrame(width, height, offset, byteLength) ->
                            let ptr = shared.Pointer + nativeint offset
                            replaceImage width height ptr
                            images.Trigger {
                                x = 0; y = 0
                                width = width; height = height
                                totalWidth = width; totalHeight = height
                                byteLength = byteLength
                                data = ptr
                            }

                        | PartialFrame(width, height, dx, dy, dw, dh, offset, byteLength) ->
                            let ptr = shared.Pointer + nativeint offset
                            updateImage dx dy dw dh ptr
                            images.Trigger {
                                x = dx; y = dy
                                width = dw; height = dh
                                totalWidth = width; totalHeight = height
                                byteLength = byteLength
                                data = ptr
                            }
                        | ChangeCursor name ->
                            cursorName <- name
                            cursorChanged.Trigger(name)

                        | Result(id, value) ->
                            match waiters.TryRemove id with
                            | (true, waiter) -> waiter value
                            | _ -> ()

                        | Unknown msg ->
                            unknownMessages.Trigger msg
                            
            },
            cancellationToken = cancel.Token
        )

    static member Init() =
        Aardium.init()

    static member Init(path : string) =
        Aardium.initPath path

    static member Logger 
        with get() = logger
        and set l = logger <- l
        
    [<CLIEvent>]
    member x.UnknownMessageReceived = unknownMessages.Publish

    [<CLIEvent>]
    member x.CursorChanged = cursorChanged.Publish

    [<CLIEvent>]
    member x.Resized = resize.Publish

    member x.Url
        with get() = url
        and set u =
            
            url <- u

    member x.CursorName = cursorName
    member x.Width = width
    member x.Height = height

    member x.LastImage = lastImage

    member x.ReadPixel(pixel : V2i) =
        let img = lastImage
        if pixel.AllGreaterOrEqual 0 && pixel.AllSmaller img.Size then
            img.GetMatrix<C4b>().[pixel]
        else
            C4b(0uy, 0uy, 0uy, 0uy)

    member x.Subscribe(obs : IObserver<OfflerImageInfo>) =
        let s = images.Publish.Subscribe obs //(fun img -> if gotFullFrame then obs.OnNext img)
        ws.Send (Command.pickle RequestFullFrame) |> Async.RunSynchronously

        s

    member x.Subscribe(callback : OfflerImageInfo -> unit) =
        x.Subscribe {
            new IObserver<OfflerImageInfo> with
                member x.OnNext img = callback img
                member x.OnError _ = ()
                member x.OnCompleted() = ()
        }

    member x.Resize(newWidth : int, newHeight : int) =
        if newWidth <> width || newHeight <> height then
            width <- newWidth
            height <- newHeight
            let cmd = Command.pickle (Resize(newWidth, newHeight))
            ws.Send cmd |> Async.RunSynchronously
            resize.Trigger(newWidth, newHeight)

    member x.Dispose() =
        if not isDisposed then  
            isDisposed <- true
            lastImage <- PixImage<byte>(Col.Format.BGRA, V2i.II)
            cancel.Cancel()
            try receiver.Wait() with _ -> ()
            try ws.CloseAsync(WebSocketCloseStatus.Empty, "bye", CancellationToken.None).Wait() with _ -> ()
            cancel.Dispose()
            shared.Dispose()
            releaseServer()

    member _.OpenDevTools() =
        if not isDisposed then 
            OpenDevTools |> Command.pickle |> ws.Send |> Async.RunSynchronously

    member _.MouseMove(x : int, y : int, ctrl : bool, alt : bool, shift : bool) =
        if not isDisposed then 
            MouseMove { x = x; y = y; ctrl = ctrl; alt = alt; shift = shift } |> Command.pickle |> ws.Send |> Async.RunSynchronously
        
    member _.MouseEnter(x : int, y : int, ctrl : bool, alt : bool, shift : bool) =
        if not isDisposed then 
            MouseEnter { x = x; y = y; ctrl = ctrl; alt = alt; shift = shift } |> Command.pickle |> ws.Send |> Async.RunSynchronously
        
    member _.MouseLeave(x : int, y : int, ctrl : bool, alt : bool, shift : bool) =
        if not isDisposed then 
            MouseLeave { x = x; y = y; ctrl = ctrl; alt = alt; shift = shift } |> Command.pickle |> ws.Send |> Async.RunSynchronously
        
    member _.MouseDown(x : int, y : int, button : MouseButton, ctrl : bool, alt : bool, shift : bool) =
        if not isDisposed then 
            MouseDown({ x = x; y = y; ctrl = ctrl; alt = alt; shift = shift }, button, 0) |> Command.pickle |> ws.Send |> Async.RunSynchronously

    member _.MouseUp(x : int, y : int, button : MouseButton, ctrl : bool, alt : bool, shift : bool) =
        if not isDisposed then 
            MouseUp({ x = x; y = y; ctrl = ctrl; alt = alt; shift = shift },button) |> Command.pickle |> ws.Send |> Async.RunSynchronously

    member _.MouseClick(x : int, y : int, button : MouseButton, count : int, ctrl : bool, alt : bool, shift : bool) =
        if not isDisposed then 
            match button with
            | Right ->
                ContextMenu({ x = x; y = y; ctrl = ctrl; alt = alt; shift = shift },button, count) |> Command.pickle |> ws.Send |> Async.RunSynchronously
            | _ ->
                MouseDown({ x = x; y = y; ctrl = ctrl; alt = alt; shift = shift },button, count) |> Command.pickle |> ws.Send |> Async.RunSynchronously
                MouseUp({ x = x; y = y; ctrl = ctrl; alt = alt; shift = shift },button) |> Command.pickle |> ws.Send |> Async.RunSynchronously
            
    member _.MouseWheel(x : int, y : int, deltaX : float, deltaY : float, ctrl : bool, alt : bool, shift : bool) =
        if not isDisposed then 
            MouseWheel({ x = x; y = y; ctrl = ctrl; alt = alt; shift = shift }, deltaX, deltaY) |> Command.pickle |> ws.Send |> Async.RunSynchronously
        
    member _.KeyDown(code : string) =
        if not isDisposed then 
            KeyDown(code) |> Command.pickle |> ws.Send |> Async.RunSynchronously
        
    member _.KeyUp(code : string) =
        if not isDisposed then 
            KeyUp(code) |> Command.pickle |> ws.Send |> Async.RunSynchronously
        
    member _.Input(code : string) =
        if not isDisposed then 
            Input(code) |> Command.pickle |> ws.Send |> Async.RunSynchronously

    member x.SetFocus(focus : bool) =
        SetFocus focus |> Command.pickle |> ws.Send |> Async.RunSynchronously

    member x.StartJavascript(js : string) =
        if not isDisposed then 
            Custom(None, js) |> Command.pickle |> ws.Send |> Async.RunSynchronously
        
    member x.RunJavascript(js : string) =
        if not isDisposed then 
            async {
                let id = Guid.NewGuid() |> string
                let res = waitFor id
                do! Custom(Some id, js) |> Command.pickle |> ws.Send
                return! res
            
            }
        else
            raise <| System.ObjectDisposedException "OffscreenBrowser"

    interface IDisposable with
        member x.Dispose() = x.Dispose()

    interface IObservable<OfflerImageInfo> with
        member x.Subscribe obs = x.Subscribe obs

    new (info : OfflerInfo) =
        let s = getServer()

        let ws = new ClientWebSocket()
        ws.ConnectAsync(Uri (sprintf "ws://127.0.0.1:%d/" s.Port), CancellationToken.None).Wait()

        let mapSize = 32L <<< 20
        let mapping = SharedMemory.createNew mapSize

        let command = 
            sprintf "{ \"command\": \"init\", \"mapName\": \"%s\", \"mapSize\": %d, \"width\": %d, \"height\": %d, \"url\": \"%s\" }" 
                mapping.Name 
                mapping.Size
                info.width
                info.height
                info.url

        ws.Send command |> Async.RunSynchronously
        let reply = ws.ReceiveString() |> Async.RunSynchronously
        match Message.unpickle reply with
        | InitComplete ->
            new Offler(ws, mapping, info.url, info.width, info.height)
        | _ ->
            failwithf "initialization failed: %A" reply
            new Offler(ws, mapping, info.url, info.width, info.height)
