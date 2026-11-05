namespace Tests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.NetworkInformation
open System.Threading
open System.Threading.Tasks
open Aardium
open Expecto

module RemoteDebugging =

    module Cases =

        /// The page closes its window after a while, so Aardium quits by itself (hideDock: on macOS too).
        let private closingPage seconds = $"data:text/html,<script>setTimeout(() => window.close(), {seconds * 1000})</script>"

        /// Chromium writes the port it chose for remote-debugging-port=0 into the profile folder: "<port>\n<browser path>".
        let private activePort folder =
            let file = Path.Combine(folder, "DevToolsActivePort")
            if File.Exists file then
                try
                    match File.ReadAllLines file with
                    | [||] -> None
                    | lines -> match Int32.TryParse lines[0] with | true, port when port > 0 -> Some port | _ -> None
                with :? IOException -> None
            else None

        let private within (seconds: float) (probe: unit -> 'a option) =
            let deadline = DateTime.UtcNow.AddSeconds seconds
            let rec loop () =
                match probe () with
                | Some value -> Some value
                | None when DateTime.UtcNow < deadline -> Thread.Sleep 100; loop ()
                | None -> None
            loop ()

        /// With remoteDebuggingPort 0, Chromium picks a free port, writes it into the userData folder and answers DevTools
        /// on the loopback interface only.
        let portInProfile () =
            Aardium.initLocal()
            let folder = Directory.CreateTempSubdirectory("aardium-remote-debugging-").FullName
            try
                let run = Task.Run(fun () -> Aardium.run { url (closingPage 15); width 320; height 240; hideDock true; userData folder; remoteDebuggingPort 0 })
                let port = within 30.0 (fun () -> activePort folder)
                Expect.isSome port $"No DevToolsActivePort in {folder}"
                let port = port.Value
                use http = new HttpClient(Timeout = TimeSpan.FromSeconds 5.0)
                let version = http.GetStringAsync($"http://127.0.0.1:{port}/json/version").GetAwaiter().GetResult()
                Expect.stringContains version "\"Browser\"" "DevTools did not answer /json/version"
                let listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners() |> Array.filter (fun e -> e.Port = port)
                Expect.isNonEmpty listeners $"Nothing listens on port {port}"
                for listener in listeners do
                    Expect.isTrue (IPAddress.IsLoopback listener.Address) $"DevTools listens on {listener}, not on the loopback interface only"
                Expect.isTrue (run.Wait(TimeSpan.FromSeconds 60.0)) "Aardium did not quit after its window closed"
            finally
                Directory.Delete(folder, true)

        /// Without remoteDebuggingPort, Aardium opens no DevTools port: CDP gives full control of the page.
        let noPortByDefault () =
            Aardium.initLocal()
            let folder = Directory.CreateTempSubdirectory("aardium-remote-debugging-").FullName
            try
                let run = Task.Run(fun () -> Aardium.run { url (closingPage 3); width 320; height 240; hideDock true; userData folder })
                Expect.isTrue (run.Wait(TimeSpan.FromSeconds 60.0)) "Aardium did not quit after its window closed"
                Expect.isTrue (File.Exists(Path.Combine(folder, "Local State"))) $"No Chromium profile in {folder}"
                Expect.isFalse (File.Exists(Path.Combine(folder, "DevToolsActivePort"))) "A DevTools port was opened without remoteDebuggingPort"
            finally
                Directory.Delete(folder, true)

    [<Tests>]
    let tests =
        testList "RemoteDebugging" [
            testCase "Port 0 picks a free port, written into the profile, on loopback" Cases.portInProfile
            testCase "No port without the option" Cases.noPortByDefault
        ] |> testSequenced
