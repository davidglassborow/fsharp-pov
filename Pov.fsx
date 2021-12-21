open System

module Model =

  /// X,Y,Z for locations in Pov
  type Vec3 = float * float * float

  /// Marker interface to specify the type can be rendered into a scene file
  type IPov = interface end

  type PovValues = Map<string,string>

  /// Optional interface to specify the type will handle it's own serialisation
  type IPovSerialisation =
    inherit IPov
    abstract member ToScene: PovValues -> string

  /// POV camera
  type CameraConfig =  { 
      Location : Vec3
      LookAt : Vec3 
  } with interface IPov

  /// Colours - TODO: add other representations 
  type Colour = 
    | Rgb of red:float * green:float * blue:float
    interface IPovSerialisation with
        member this.ToScene _ =
          match this with
          | Rgb (x,y,z) -> $"rgb <{x},{y},{z}>"

  /// POV lightsource - http://www.povray.org/documentation/3.7.0/r3_4.html#r3_4_4
  type LightSourceConfig = {
      Location : Vec3
      Color : Colour
  } with interface IPovSerialisation with       // First line of light source has no name, and is location
            member _.ToScene kv =
                let firstLine = kv["location"] + "\n" 
                let kv = kv |> Map.remove("location")
                let otherValues = kv |> Seq.map (fun kv -> $"  {kv.Key} {kv.Value}") |> String.concat "\n"
                firstLine + otherValues


  /// POV pigment http://www.povray.org/documentation/3.7.0/r3_4.html#r3_4_6_1
  // TODO: add color_map, pigment_map etc.
  type Pigment = {
    Color : Colour
  } with interface IPov


  /// POV sphere - http://www.povray.org/documentation/3.7.0/r3_4.html#r3_4_5_1_12
  type SphereConfig = {
      Location : Vec3
      Radius : int
      Pigment : Pigment
  } with interface IPovSerialisation with       // First line of sphere has no name, and is location, radius
            member _.ToScene kv =
                let firstLine = kv["location"] + "," + kv["radius"] + "\n"
                let kv = kv |> Map.remove("location") |> Map.remove("radius")
                let otherValues = kv |> Seq.map (fun kv -> $"  {kv.Key} {kv.Value}") |> String.concat "\n"
                firstLine + otherValues

  /// POV box - http://www.povray.org/documentation/3.7.0/r3_4.html#r3_4_5_1_2
  type BoxConfig = {
      Corner1 : Vec3
      Corner2 : Vec3
      Pigment : Pigment
  } with interface IPovSerialisation with       // First line of box has no name, and is corner1, corner2
            member _.ToScene kv =
                let firstLine = kv["corner1"] + "," + kv["corner2"] + "\n"
                let kv = kv |> Map.remove("corner1") |> Map.remove("corner2")
                let otherValues = kv |> Seq.map (fun kv -> $"  {kv.Key} {kv.Value}") |> String.concat "\n"
                firstLine + otherValues

open Model

module Builders =

  type CameraBuilder() =
      member _.Yield _ = { 
          Location = 0,0,0
          LookAt = 0,0,0 }        
      [<CustomOperation "location">]
      member _.Location (state:CameraConfig, l) = { state with Location = l }
      [<CustomOperation "look_at">]
      member _.LookAt (state:CameraConfig, l) = {state with LookAt = l}

  type LightSourceBuilder() =
      member _.Yield _ = { 
          Location = 0,0,0
          Color = Rgb(1,1,1)  }        
      [<CustomOperation "location">]
      member _.Location (state:LightSourceConfig, l) = { state with Location = l }
      [<CustomOperation "color">]
      member _.Color (state:LightSourceConfig, l) = { state with Color = l }


  type SphereBuilder() =
      member _.Yield _ = { 
          Radius = 1
          Location = 0,0,0
          Pigment = { Color = Rgb(1.,1.,1.) }}        
      [<CustomOperation "location">]
      member _.Location (state:SphereConfig, l) = { state with Location = l }
      [<CustomOperation "radius">]
      member _.Radius (state:SphereConfig, l) = {state with Radius = l}
      [<CustomOperation "pigment">]
      member _.Pigment (state:SphereConfig, l) = {state with Pigment = { Color = l}}


  type BoxBuilder() =
      member _.Yield _ = { 
          Corner1 = 0,0,0
          Corner2 = 1,1,1
          Pigment = { Color = Rgb(1.,1.,1.) }}
      [<CustomOperation "corner1">]
      member _.Location (state:BoxConfig, l) = { state with Corner1 = l }
      [<CustomOperation "corner2">]
      member _.Radius (state:BoxConfig, l) = {state with Corner2 = l}
      [<CustomOperation "pigment">]
      member _.Pigment (state:BoxConfig, l) = {state with Pigment = { Color = l }}


  let light_source = LightSourceBuilder()
  let camera = CameraBuilder()
  let sphere = SphereBuilder()
  let box = BoxBuilder()

/// Responsible for converting F# records into POV scene description
module Serialisation = 

    /// e.g. convert LookAt to look_at
    let private toSnakeCase (x:string) = 
        let lower = Char.ToLower >> string
        seq {
            yield lower x[0]
            for i in 1..(x.Length-1) do
            if Char.IsUpper x[i] then yield "_" + lower x[i]
            else yield lower x[i]
        } |> String.concat "" 

    let rec private determineKeyValues objectType obj =
        // For exact property of the type, work out it's representation
        [ for p in (objectType:Type).GetProperties() ->
            let name = toSnakeCase p.Name
            let valueType = p.PropertyType
            match p.GetValue(obj:obj) with
            | :? IPovSerialisation as ps -> 
                name, ps.ToScene (determineKeyValues valueType ps)
            | :? Vec3 as (x,y,z) -> 
                name,  $"<{x},{y},{z}>"
            | :? Int32 as i -> 
                name, $"{i}"
            | :? Double as d ->
                name, $"{d}"
            | :? String as s -> 
                name, $"{s}"
            | :? IPov as p ->
                name, "{" + (determineKeyValues valueType p |> Seq.map (fun kv -> $"{kv.Key} {kv.Value}") |> String.concat "\n") + "}"
            | _ -> 
                failwith $"Need to handle {valueType.Name}" ] |> Map

    /// Convert any type marked with IPov into POV scene language
    let toScene (i:IPov) =
        let objectType = i.GetType()
        let name = objectType.Name.Replace("Config","") |> toSnakeCase
        let sb = Text.StringBuilder()

        let keyValues = determineKeyValues objectType i

        let add msg = sb.AppendLine(msg:string) |> ignore
        add $"{name} {{"

        match i with
        | :? IPovSerialisation as ps -> ps.ToScene keyValues |> add
        | _ -> keyValues |> Seq.iter (fun kv -> add $"  {kv.Key} {kv.Value}") 

        add "}"
        string sb

    /// Convert a collecion of items into a singe scene file
    let toSceneFile (items: #seq<IPov>) =
        items 
        |> Seq.map toScene
        |> String.concat "\n"

open Builders
open Serialisation

module Povray =

  open System.IO
  open System.Diagnostics
  open System.Text

  let shell cmd arguments =
      printfn $"Shelling {cmd} {arguments}"
      let startup = new ProcessStartInfo(
          FileName = cmd,
          Arguments = arguments,
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = false )

      use proc = new Process(StartInfo = startup)
      let stdOut = StringBuilder()
      let stdErr = StringBuilder()

      let outputHandler (sb: StringBuilder) (_sender:obj) (args:DataReceivedEventArgs) = sb.Append(args.Data) |> ignore
      proc.OutputDataReceived.AddHandler(DataReceivedEventHandler (outputHandler stdOut))
      proc.ErrorDataReceived.AddHandler(DataReceivedEventHandler (outputHandler stdErr))
      let started = 
          try
              proc.Start()
          with | ex ->
              ex.Data.Add("filename", cmd)
              reraise()
      if not started then
          failwithf "Failed to start process %s" cmd
      proc.BeginOutputReadLine()
      proc.BeginErrorReadLine()
      proc.WaitForExit()
      proc.ExitCode, stdOut.ToString(), stdErr.ToString()

  let render txt  =
      File.WriteAllText("scene.pov", txt)
      let checkResult (result,out,err) =
          printfn $"Result {result} {err}"
          if result <> 0 then failwithf "Error shelling, %s" err
      shell "povray" "scene.pov" |> checkResult
      shell "open" "scene.png"  |> checkResult

// Render example scene from https://github.com/spcask/pov-ray-tracing/blob/master/src/scene01.pov
toSceneFile [  
 
  camera {
    location (0,0,0)
    look_at (0,0,10)
  }

  // Yellow ball
  sphere {
    location (-6,0,20)
    radius 5
    pigment (Rgb(0.99, 0.83, 0.40))
  }

  // Blue ball
  sphere {
    location (0.2,0,10)
    radius 2
    pigment (Rgb(0.42,0.5,0.99))
  }

  // orange ball
  sphere {
    location (4,1,10)
    radius 1
    pigment (Rgb(0.82, 0.40, 0.10))
  }

  // Red box
  box {
    corner1 (-2,-2,8)
    corner2 (-1,-1,6)
    pigment (Rgb(0.9, 0, 0.06))
  }

  // green box
  box {
    corner1 (1, 1, 8)
    corner2 (2, 2, 6)
    pigment (Rgb(0.09, 0.76, 0.16))
  }

  // pale box
  box {
    corner1 (8, -5, 30)
    corner2 (12, -1, 35)
    pigment (Rgb(0.70, 0.50, 0.60))
  }

  // Top right corner light source (behind the camera). This casts the
  // shadow of the green box on the blue ball and that of the blue ball on
  // the yellow one.
  light_source {
      location (5,5,-10)
      color (Rgb(1, 1, 1))
  }

  // Light source at the left side of the scene. This light source is also
  // behind the camera. This casts the smaller shadow of the red box on
  // the blue ball, that of the green box on the orange ball and that of
  // the blue ball on the pale pink box.
  light_source {
      location (-5,0,-10)
      color (Rgb(0.4, 0.4, 0.4))
  }

  // Light source at the bottom right corner of the scene. This light
  // source is present slightly in front of the camera. This casts the
  // longer shadow of the red box on the blue ball.
  light_source {
      location (-5,5,2)
      color (Rgb(0.4, 0.4, 0.4))
  }
] 
|> fun scene -> printfn "%s" scene; scene
|> Povray.render
