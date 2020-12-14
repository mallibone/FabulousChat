// Copyright Fabulous contributors. See LICENSE.md for license.
namespace FabulousChat

open System
open System.Text.Json
open Fabulous
open Fabulous.XamarinForms
open Xamarin.Forms
open Microsoft.AspNetCore.SignalR.Client

[<AutoOpen>]
module Core =
    type ChatMessage =
        {Username : string
         Message : string
         Timestamp : DateTimeOffset}
        
    type Msg = 
        | Connected of HubConnection
        | SendMessage
        | MessageSent
        | MessageChanged of string
        | MessageReceived of ChatMessage

module SignalR =
    let connectToServer =
        let connection = HubConnectionBuilder()
                             .WithUrl("https://signalr-gnabber-function.azurewebsites.net/api")
                             .WithAutomaticReconnect()
                             .Build()
    
        async {
              do! connection.StartAsync() |> Async.AwaitTask
              return connection
        }
        
    let startListeningToChatMessages (connection:HubConnection) dispatch =
        let handleReceivedMessage (msg:string) =
            dispatch (Msg.MessageReceived (JsonSerializer.Deserialize<ChatMessage>(msg)))
            
        connection.On<string>("NewMessage", handleReceivedMessage)
        
    let sendMessage (connection:HubConnection) (message:ChatMessage) =
        async {
            do! connection.SendAsync("SendMessage", message) |> Async.AwaitTask
        }

module App =
    type AppState = Ready | Busy //| Error of string
    type Model = 
      { Username: string
        ChatMessage: string
        AppState: AppState
        Messages: ChatMessage List
        SignalRConnection: HubConnection Option }

    let initModel = { Username = "Gnabber"; ChatMessage = ""; Messages = []; SignalRConnection=None; AppState = Busy }

    let initSignalR =
        async {
            let! connection = SignalR.connectToServer
            return Msg.Connected connection
        }
    let init () = initModel, Cmd.ofAsyncMsg (initSignalR)

    let sendMessage (connection:HubConnection) username chatMessage =
        let chatMessage = {Username = username; Message = chatMessage; Timestamp = DateTimeOffset.Now }
        async {
            do! (SignalR.sendMessage connection chatMessage)
            return MessageSent
        }
        |> Cmd.ofAsyncMsg

    let update msg model =
        match msg with
        | MessageReceived chatMessage -> { model with Messages = chatMessage::model.Messages }, Cmd.none
        | MessageSent -> { model with AppState = Ready }, Cmd.none
        | MessageChanged newMessage -> { model with ChatMessage = newMessage }, Cmd.none
        | Connected connection -> { model with SignalRConnection = Some connection; AppState = Ready }, Cmd.none
        | SendMessage ->
                {model with AppState = Busy},
                match model.SignalRConnection with
                | Some(connection) -> (sendMessage connection model.Username model.ChatMessage)
                | None -> Cmd.none

    let view (model: Model) dispatch =
        View.ContentPage(
            content = View.Label(text = "Hello world")
        )
//        View.ContentPage(
//          content = View.StackLayout(padding = Thickness 20.0, verticalOptions = LayoutOptions.Center,
//            children = [ 
//                View.Label(text = sprintf "%d" model.Count, horizontalOptions = LayoutOptions.Center, width=200.0, horizontalTextAlignment=TextAlignment.Center)
//                View.Button(text = "Increment", command = (fun () -> dispatch Increment), horizontalOptions = LayoutOptions.Center)
//                View.Button(text = "Decrement", command = (fun () -> dispatch Decrement), horizontalOptions = LayoutOptions.Center)
//                View.Label(text = "Timer", horizontalOptions = LayoutOptions.Center)
//                View.Switch(isToggled = model.TimerOn, toggled = (fun on -> dispatch (TimerToggled on.Value)), horizontalOptions = LayoutOptions.Center)
//                View.Slider(minimumMaximum = (0.0, 10.0), value = double model.Step, valueChanged = (fun args -> dispatch (SetStep (int (args.NewValue + 0.5)))), horizontalOptions = LayoutOptions.FillAndExpand)
//                View.Label(text = sprintf "Step size: %d" model.Step, horizontalOptions = LayoutOptions.Center) 
//                View.Button(text = "Reset", horizontalOptions = LayoutOptions.Center, command = (fun () -> dispatch Reset), commandCanExecute = (model <> initModel))
//            ]))

    // Note, this declaration is needed if you enable LiveUpdate
    let program =
        XamarinFormsProgram.mkProgram init update view
#if DEBUG
        |> Program.withConsoleTrace
#endif

type App () as app = 
    inherit Application ()

    let runner = 
        App.program
        |> XamarinFormsProgram.run app

#if DEBUG
    // Uncomment this line to enable live update in debug mode. 
    // See https://fsprojects.github.io/Fabulous/Fabulous.XamarinForms/tools.html#live-update for further  instructions.
    //
    //do runner.EnableLiveUpdate()
#endif    

    // Uncomment this code to save the application state to app.Properties using Newtonsoft.Json
    // See https://fsprojects.github.io/Fabulous/Fabulous.XamarinForms/models.html#saving-application-state for further  instructions.
#if APPSAVE
    let modelId = "model"
    override __.OnSleep() = 

        let json = Newtonsoft.Json.JsonConvert.SerializeObject(runner.CurrentModel)
        Console.WriteLine("OnSleep: saving model into app.Properties, json = {0}", json)

        app.Properties.[modelId] <- json

    override __.OnResume() = 
        Console.WriteLine "OnResume: checking for model in app.Properties"
        try 
            match app.Properties.TryGetValue modelId with
            | true, (:? string as json) -> 

                Console.WriteLine("OnResume: restoring model from app.Properties, json = {0}", json)
                let model = Newtonsoft.Json.JsonConvert.DeserializeObject<App.Model>(json)

                Console.WriteLine("OnResume: restoring model from app.Properties, model = {0}", (sprintf "%0A" model))
                runner.SetCurrentModel (model, Cmd.none)

            | _ -> ()
        with ex -> 
            App.program.onError("Error while restoring model found in app.Properties", ex)

    override this.OnStart() = 
        Console.WriteLine "OnStart: using same logic as OnResume()"
        this.OnResume()
#endif


