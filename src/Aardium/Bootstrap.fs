namespace Aardium

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open System.Diagnostics
open Microsoft.FSharp.Reflection


[<Struct>]
type Progress(received : int64, total : int64) =
    member x.Received = received
    member x.Total = total
    member x.Relative = float received / float total

    override x.ToString() =
        sprintf "%.2f%%" (100.0 * float received / float total)

module Tools =
    
    type private Message =
        | Log of string
        | Error of string

    let unzip (file : string) (folder : string) =
        try
            if Directory.Exists folder then Directory.Delete(folder, true)
            ZipFile.ExtractToDirectory(file, folder)
            
        with _ ->
            if Directory.Exists folder then Directory.Delete(folder, true)
            reraise()

    let download (progress : Progress -> unit) (url : string) (file : string) =
        try
            use c = new HttpClient()

            let response = c.GetAsync(System.Uri url, HttpCompletionOption.ResponseHeadersRead).Result
            let len = 
                let f = response.Content.Headers.ContentLength
                if f.HasValue then f.Value
                else 1L <<< 30
                
            let mutable lastProgress = Progress(0L,len)
            progress lastProgress
            let sw = System.Diagnostics.Stopwatch.StartNew()


            use stream = response.Content.ReadAsStreamAsync().Result
            if File.Exists file then File.Delete file
            use output = File.OpenWrite(file)


            let buffer : byte[] = Array.zeroCreate (4 <<< 20)
            
            let mutable remaining = len
            let mutable read = 0L
            while remaining > 0L do
                let cnt = int (min remaining buffer.LongLength)
                let r = stream.Read(buffer, 0, cnt)
                output.Write(buffer, 0, r)
            
                remaining <- remaining - int64 r
                read <- read + int64 r


                let p = Progress(read, len)
                if sw.Elapsed.TotalSeconds >= 0.1 && p.Relative - lastProgress.Relative > 0.1 then
                    progress p
                    lastProgress <- p
                    sw.Restart()
                    
        with _ ->
            if File.Exists file then File.Delete file
            reraise()
          
    let startThread (f : unit -> unit) =
        let t = new Thread(ThreadStart(f), IsBackground = true)
        t.Start()

    let start (file : string) (logger : bool -> string -> unit) (args : string[]) =
        let info = 
            ProcessStartInfo(
                file, 
                Arguments = String.concat " " args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            )
            
        info.EnvironmentVariables.["ELECTRON_ENABLE_LOGGING"] <- "1"
        info.Environment.["ELECTRON_ENABLE_LOGGING"] <- "1"

        let proc = Process.Start(info)
        FSys.Process.attachChild proc 

        proc.OutputDataReceived.Add (fun e ->
            if not (String.IsNullOrWhiteSpace e.Data) then
                logger false e.Data
        )
        
        proc.ErrorDataReceived.Add (fun e ->
            if not (String.IsNullOrWhiteSpace e.Data) then
                logger true e.Data
        )
        
        proc.BeginOutputReadLine()
        proc.BeginErrorReadLine()
        
        proc

    let exec (file : string) (logger : bool -> string -> unit) (args : string[]) =
        let info = 
            ProcessStartInfo(
                file, 
                Arguments = String.concat " " args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            )
            
        info.EnvironmentVariables.["ELECTRON_ENABLE_LOGGING"] <- "1"
        info.Environment.["ELECTRON_ENABLE_LOGGING"] <- "1"

        let proc = Process.Start(info)
        FSys.Process.attachChild proc

        use log = new System.Collections.Concurrent.BlockingCollection<Message>()

        proc.OutputDataReceived.Add (fun e ->
            if not (String.IsNullOrWhiteSpace e.Data) then
                log.Add (Log e.Data)
        )
        
        proc.ErrorDataReceived.Add (fun e ->
            if not (String.IsNullOrWhiteSpace e.Data) then
                log.Add (Error e.Data)
        )
        
        proc.BeginOutputReadLine()
        proc.BeginErrorReadLine()
        
        let cancel = new CancellationTokenSource()

        startThread (fun () ->
            proc.WaitForExit()
            cancel.Cancel()
        )
        
        try
            while true do
                let msg = log.Take(cancel.Token)
                match msg with
                    | Log msg -> logger false msg
                    | Error msg -> logger true msg
        with :? OperationCanceledException ->
            ()


type AardiumConfig =
    {
        width       : Option<int>
        height      : Option<int>
        url         : Option<string>
        debug       : bool
        icon        : Option<string>
        title       : Option<string>
        menu        : bool
        fullscreen  : bool
        hideDock    : bool
        autoclose   : bool
        experimental: bool
        woptions    : Option<string>
        log         : bool -> string -> unit
    }

module AardiumConfig =
    let empty = 
        {
            width = None
            height = None
            url = None
            debug = true
            icon = None
            title = None
            menu = false
            fullscreen = false
            experimental = false
            hideDock = false
            autoclose = false
            woptions = None
            log = fun isError ln -> ()
        }

    let internal toArgs (cfg : AardiumConfig) =
        [|
        
            match cfg.debug with
            | true -> yield "--debug"
            | false -> ()

            match cfg.menu with
            | true -> yield "--menu"
            | false -> ()

            match cfg.fullscreen with
            | true -> yield "--fullscreen"
            | false -> ()

            match cfg.experimental with
            | true -> yield "--experimental"
            | false -> ()

            match cfg.width with
            | Some w -> yield! [| "--width=" + string w |]
            | None -> ()

            match cfg.height with
            | Some h -> yield! [| "--height=" + string h |]
            | None -> ()

            match cfg.url with
            | Some url -> yield! [| "--url=\"" + url + "\"" |]
            | None -> ()

            
            match cfg.title with    
            | Some t -> yield! [| "--title=\"" + t + "\"" |]
            | None -> ()
                
            match cfg.icon with    
            | Some i -> yield! [| "--icon=\"" + i + "\"" |]
            | None -> ()

            match cfg.hideDock with    
            | true -> yield! [| "--hideDock"|]
            | false -> ()

            match cfg.autoclose with    
            | true -> yield! [| "--autoclose"|]
            | false -> ()

            match cfg.woptions with
            | Some w -> yield "--woptions=\""  + w.Replace("\"", "\\\"") + "\""
            | None -> ()
                

        |]

type AardiumOffscreenServer internal (proc : Process, port : int) =
    
    member x.IsRunning = not proc.HasExited
    member x.Port = port
    member x.Stop() = if not proc.HasExited then proc.Kill()

    member x.Dispose() = x.Stop()
    interface System.IDisposable with
        member x.Dispose() = x.Dispose()

module Aardium =
    open System.Runtime.InteropServices

    let feed = "https://www.nuget.org/api/v2/package"
    let packageBaseName = "Aardium"
    let version = 
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "2.0.5"
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "2.0.5"
        elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then "2.0.6"
        else failwith "unsupported platform"
    
    [<Literal>]
    let private Win = "Win32"
    [<Literal>]
    let private Linux = "Linux"
    [<Literal>]
    let private Darwin = "Darwin"

    let private platform =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then Win
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then Linux
        elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then Darwin
        else failwith "unsupported platform"

    let private arch =
        match sizeof<nativeint> with
            | 4 -> "x86"
            | 8 -> "x64"
            | v -> failwithf "bad bitness: %A" v

    let private packageName = sprintf "%s-%s-%s" packageBaseName platform arch

    let private exeName =
        match platform with
            | Win -> "Aardium.exe"
            | Linux -> "Aardium"
            | Darwin -> "Aardium.app/Contents/MacOS/Aardium"
            | _ -> failwith "unsuporrted platform"

    //let private cachePath =
    //    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aardium")

    let mutable private cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aardium")
    let mutable private executablePath = ""

    module Libc =
        open System.Runtime.InteropServices
        [<DllImport("libc")>]
        extern int chmod(string path, int mode)
        
    let initPath (pp : string) =
        if executablePath = "" then
            let path = Path.GetFullPath pp
            cachePath <- path
            let info = DirectoryInfo cachePath
            if not info.Exists then info.Create()

            if File.Exists(Path.Combine(path, exeName)) then
                executablePath <- Path.Combine(path, exeName)
            else
                let aardiumPath = Path.Combine(cachePath, arch, version)

                let info = DirectoryInfo aardiumPath
                if info.Exists && File.Exists(Path.Combine(aardiumPath, "tools", exeName)) then
                    executablePath <- Path.Combine(aardiumPath, "tools", exeName)

                else
                    info.Create()
                    let fileName = sprintf "%s.%s.nupkg" packageName version
                    let tempFile = Path.Combine(cachePath, arch, fileName)
                    let url = sprintf "%s/%s/%s" feed packageName version

                    Console.WriteLine("downloading aardium: " + url)
                    Tools.download (fun s -> Console.Write("\rdownloading aardium ... {0}% ", sprintf "%.0f" (s.Relative * 100.0))) url tempFile
                    Console.WriteLine("")
                    Console.WriteLine("downloaded: " + tempFile)

                    Tools.unzip tempFile aardiumPath
                    match platform with
                        | Darwin ->
                            let zip = Path.Combine(aardiumPath, "Aardium-1.0.0-mac")
                            let outDir = Path.Combine(aardiumPath, "tools")
                            Console.WriteLine(sprintf "unziping %s to %s" zip outDir)
                            Tools.unzip zip outDir
                            Console.WriteLine("unzipped")
                        | Linux -> 
                            let command = "-zxvf Aardium-" + platform + "-" + arch + ".tar.gz -C ./"
                            let outDir = Path.Combine(aardiumPath, "tools")
                            Console.WriteLine("workdir: " + outDir + "- " + "tar " + command)
                            let info = ProcessStartInfo("tar", command)
                            info.WorkingDirectory <- outDir
                            info.UseShellExecute <- false
                            info.CreateNoWindow <- true
                            info.RedirectStandardError <- true
                            info.RedirectStandardInput <- true
                            info.RedirectStandardOutput <- true
                            let proc = System.Diagnostics.Process.Start(info)
                            proc.WaitForExit()
                            if proc.ExitCode <> 0 then 
                                proc.StandardError.ReadToEnd() |> printfn "ERROR: %s"
                            Console.WriteLine("untared")
                        | _ -> ()


                    let path = Path.Combine(aardiumPath, "tools", exeName)

                    if File.Exists(path) then
                        executablePath <- path
                    else
                        failwithf "could not find executable after download: %s" path

    let init() =
        let path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), (sprintf "Aardium-%s" (version.Replace(".","_"))))
        Console.WriteLine("Init Aardium at: " + path)
        initPath path

    let runConfig (cfg : AardiumConfig)  =
        if File.Exists executablePath then
            Tools.exec executablePath cfg.log (AardiumConfig.toArgs cfg) 
        else
            failwithf "could not locate aardium"

    type AardiumBuilder() =
        member x.Yield(()) = AardiumConfig.empty

        [<CustomOperation("windowoptions")>]
        member x.WindowOptions(cfg : AardiumConfig, value : 'a) =
            if FSharpType.IsRecord(typeof<'a>, true) then
                let rec toJSON(t : Type) (value : obj) =
                    if t = typeof<string> then 
                        sprintf "\"%s\"" (unbox<string> value)
                    elif t = typeof<bool> then
                        if unbox value then "true" else "false"
                    elif t = typeof<float> || t = typeof<int> then
                        string value
                    elif FSharpType.IsRecord(t, true) then
                        FSharpType.GetRecordFields(typeof<'a>, true)
                        |> Array.map (fun f -> 
                            let value = toJSON f.PropertyType (f.GetValue value)
                            sprintf "\"%s\": %s" f.Name value
                        )
                        |> String.concat ", "
                        |> sprintf "{ %s }"
                    else
                        let seq = t.GetInterface(typedefof<seq<_>>.FullName)
                        if isNull seq then failwith "unknown type"
                        else
                            let e = (value :?> System.Collections.IEnumerable).GetEnumerator()
                            let t = seq.GetGenericArguments().[0]
                            let all = System.Collections.Generic.List<obj>()
                            while e.MoveNext() do
                                all.Add e.Current
                            all |> Seq.map (toJSON t) |> String.concat ", " |> sprintf "[%s]"
                        
                let json = toJSON typeof<'a> value
                { cfg with woptions = Some json }
            else
                failwith "bad window options"

        [<CustomOperation("url")>]
        member x.Url(cfg : AardiumConfig, url : string) =
            { cfg with url = Some url }
            
        [<CustomOperation("icon")>]
        member x.Icon(cfg : AardiumConfig, file : string) =
            { cfg with icon = Some file }
            
        [<CustomOperation("title")>]
        member x.Title(cfg : AardiumConfig, title : string) =
            { cfg with title = Some title }

        [<CustomOperation("width")>]
        member x.Width(cfg : AardiumConfig, w : int) =
            { cfg with width = Some w }
            
        [<CustomOperation("height")>]
        member x.Height(cfg : AardiumConfig, h : int) =
            { cfg with height = Some h }
            
        [<CustomOperation("size")>]
        member inline x.Size(cfg : AardiumConfig, v : ^a) =
            let w = (^a: (member P_X : int)(v))
            let h = (^a: (member P_Y : int)(v))
            { cfg with width = Some w; height = Some h }
            
        [<CustomOperation("debug")>]
        member x.Debug(cfg : AardiumConfig, v : bool) =
            { cfg with debug = v }

        [<CustomOperation("fullscreen")>]
        member x.Fullscreen(cfg : AardiumConfig, v : bool) =
            { cfg with fullscreen = v }

        [<CustomOperation("hideDock")>]
        member x.HideDock(cfg : AardiumConfig, v : bool) =
            { cfg with hideDock = v }

        [<CustomOperation("autoclose")>]
        member x.AutoClose(cfg : AardiumConfig, v : bool) =
            { cfg with autoclose = v }
            
        [<CustomOperation("menu")>]
        member x.Menu(cfg : AardiumConfig, v : bool) =
            { cfg with menu = v }
            
        [<CustomOperation("experimental")>]
        member x.Experimental(cfg : AardiumConfig, v : bool) =
            { cfg with experimental = v }

        member x.Run(cfg : AardiumConfig) =
            runConfig cfg


    let run = AardiumBuilder()

    let startOffscreenServer (port : int) (logger : bool -> string -> unit) =
        let port = 
            if port <> 0 then
                port
            else
                let l = System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)
                l.Start()
                let p = l.LocalEndpoint |> unbox<System.Net.IPEndPoint>
                let port = p.Port
                l.Stop()
                port

        if File.Exists executablePath then
            logger false (sprintf "starting server with port %d" port)
            let proc = Tools.start executablePath logger [|sprintf "--server=%d" port|]
            new AardiumOffscreenServer(proc, port)
        else
            failwith "could not locate aardium"
