module Signing
#r "packages/FAKE/tools/FakeLib.dll"
#r "packages/FSharp.Data/lib/net40/FSharp.Data.dll"
// #r "packages/FSharp.Azure.StorageTypeProvider/lib/net452/FSharp.Azure.StorageTypeProvider.dll"
// #r "packages/WindowsAzure.Storage/lib/net45/Microsoft.WindowsAzure.Storage.dll"

open System

open Fake
open Fake.ProcessHelper
open FSharp.Data
// open FSharp.Azure.StorageTypeProvider
    // type AzureStorage = AzureTypeProvider<"">
    type CsvSchema =
        CsvProvider<Schema = "Path (string), Verified (string), Date (string), Publisher (string), Company (string), Description (string), Product (string), Product Version (string), File Version (string), Machine Type (string)",
                    HasHeaders=false>

    let RunSigCheck path =
        ExecProcessAndReturnMessages (fun info ->
            info.FileName <- @".\tools\sigcheck.exe"
            info.WorkingDirectory <- Environment.CurrentDirectory
            info.Arguments <- "-nobanner -c -accepteula -e " + path
        ) (TimeSpan.FromMinutes 2.0)
        |> fun result -> result.Messages |> String.concat Environment.NewLine |> CsvSchema.Parse

    // let UploadZip filePath =
    //     let container = AzureStorage.Containers.``azure-functions-cli``
    //     container.Upload filePath |> Async.RunSynchronously

    // let PushQueueMessage (message: string) =
    //     let message = "SignAuthenticode;azure-functions-cli;" + message
    //     let queue = AzureStorage.Queues.``signing-jobs``
    //     queue.Enqueue message |> Async.RunSynchronously

    // let rec downloadFile fileName (endTime: DateTime) = async {
    //     let container = AzureStorage.Containers.``azure-functions-cli-signed``
    //     let! result = container.TryGetBlockBlob fileName
    //     match result with
    //     | Some blob -> return Some blob
    //     | None -> if DateTime.Now > endTime then return None else return! downloadFile fileName endTime
    // }

    // let PollBlobForZip fileName downloadName =
    //     let signed = (downloadFile fileName (DateTime.Now.AddMinutes 2.0)) |> Async.RunSynchronously
    //     match signed with
    //     | Some blob ->
    //         blob.Download downloadName
    //         |> Async.RunSynchronously
    //         true
    //     | None -> false