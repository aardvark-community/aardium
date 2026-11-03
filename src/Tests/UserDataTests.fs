namespace Tests

open System
open System.IO
open System.Threading.Tasks
open Aardium
open Expecto

module UserData =

    module Cases =

        /// The page closes its window, so Aardium quits by itself (hideDock: on macOS too).
        let private closingPage = "data:text/html,<script>setTimeout(() => window.close(), 3000)</script>"

        /// Aardium writes the Chromium profile ("Local State") into the folder userData names.
        let profileInFolder () =
            Aardium.initLocal()
            let folder = Directory.CreateTempSubdirectory("aardium-user-data-").FullName
            try
                let run = Task.Run(fun () -> Aardium.run { url closingPage; width 320; height 240; hideDock true; userData folder })
                Expect.isTrue (run.Wait(TimeSpan.FromSeconds 60.0)) "Aardium did not quit after its window closed"
                Expect.isTrue (File.Exists(Path.Combine(folder, "Local State"))) $"No Chromium profile in {folder}"
            finally
                Directory.Delete(folder, true)

    [<Tests>]
    let tests =
        testList "UserData" [
            testCase "Profile in the user data folder" Cases.profileInFolder
        ]
