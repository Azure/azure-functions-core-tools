// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.

open System.IO
open FSharp.Data
open System.Net

type Config = XmlProvider<"""
<configuration>
  <runtime>
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
      <dependentAssembly>
        <assemblyIdentity name="" publicKeyToken="" culture="" />
        <bindingRedirect oldVersion="" newVersion="" />
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name="" publicKeyToken="" culture="" />
        <bindingRedirect oldVersion="" newVersion="" />
      </dependentAssembly>
    </assemblyBinding>
  </runtime>
</configuration>
""">

let source = new WebClient () |> fun x -> x.DownloadString("https://raw.githubusercontent.com/Azure/azure-webjobs-sdk-script/dev/src/WebJobs.Script.WebHost/Web.config") |> Config.Parse
let dest = "..\\..\\..\\..\\src\\Azure.Functions.Cli\\App.config" |> File.ReadAllText |> Config.Parse

for assembly in source.Runtime.AssemblyBinding.DependentAssemblies do
    let destElement =
        dest.Runtime.AssemblyBinding.DependentAssemblies
        |> Array.tryFind (fun x -> x.AssemblyIdentity.Name = assembly.AssemblyIdentity.Name)
    if destElement.IsSome && (destElement.Value.BindingRedirect.NewVersion <> assembly.BindingRedirect.NewVersion || destElement.Value.BindingRedirect.OldVersion <> assembly.BindingRedirect.OldVersion) then
        printfn "Different: %s" destElement.Value.AssemblyIdentity.Name
    else if destElement.IsNone then
        printfn "Not in dest: %s" assembly.AssemblyIdentity.Name