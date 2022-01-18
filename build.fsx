#r "paket: nuget Fake.Core.Target ~> 5.21.0-alpha004"
 
open System
open Fake.Core
open Fake.Core.TargetOperators

do Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

Target.create "Default" (fun _ ->
    Trace.tracefn "sadsadsad"    
)

Target.runOrDefault "Default"