// Learn more about F# at http://fsharp.org

open System
open Aardvark.Base
open Aardium


[<EntryPoint>]
let main argv =
    Aardium.init()
    Aardium.run { 
        url "http://ask.aardvark.graphics"
    }

    0 // return an integer exit code
