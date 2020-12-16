// Copyright Fabulous contributors. See LICENSE.md for license.
namespace FabulousChat

open System
open System.Globalization
open System.Text.Json
open Fabulous
open Fabulous.XamarinForms.LiveUpdate
open Fabulous.XamarinForms
open Xamarin.Forms
open Microsoft.AspNetCore.SignalR.Client

[<AutoOpen>]
module Core =
    type AppState =
        | Ready
        | Busy //| Error of string

    type Page =
        | Login
        | Chat

    type ChatMessage =
        { Username: string
          Message: string
          Timestamp: DateTimeOffset }

    type Model =
        { CurrentPage: Page
          Username: string
          ChatMessage: string
          AppState: AppState
          Messages: ChatMessage List
          SignalRConnection: HubConnection Option }

    type Msg =
        | LoggingIn
        | LoggedIn
        | UsernameChanged of string
        | Connected of HubConnection
        | SendMessage
        | MessageSent
        | MessageChanged of string
        | MessageReceived of ChatMessage

module SignalR =
    let connectToServer =
        let connection =
            HubConnectionBuilder()
                .WithUrl(Config.SignalRUrl)
                .WithAutomaticReconnect()
                .Build()

        async {
            do! connection.StartAsync() |> Async.AwaitTask
            return connection
        }

    let startListeningToChatMessages (connection: HubConnection) dispatch =
        let handleReceivedMessage (msg: string) =
            printfn "Received message: %s" msg
            dispatch (Msg.MessageReceived(JsonSerializer.Deserialize<ChatMessage>(msg)))
            ()

        connection.On<string>("NewMessage", handleReceivedMessage)

    let sendMessage (connection: HubConnection) (message: ChatMessage) =
        async {
            let jsonMessage = JsonSerializer.Serialize(message)

            do! connection.SendAsync("SendMessage", jsonMessage)
                |> Async.AwaitTask
        }

module Login =
    let loginUser dispatch =
        async {
            let! connection = SignalR.connectToServer
            dispatch (Msg.Connected connection)

            SignalR.startListeningToChatMessages connection dispatch
            |> ignore

            dispatch (Msg.LoggedIn)
        }
        |> Async.StartImmediate

    let view model dispatch =
        View.ContentPage
            (title = "Login",
             content =
                 View.StackLayout
                     (verticalOptions = LayoutOptions.Center,
                      horizontalOptions = LayoutOptions.Center,
                      children =
                          [ View.Label(text = "Please enter your Username")
                            View.Entry
                                (text = model.Username,
                                 placeholder = "Please enter your username",
                                 textChanged = fun e -> dispatch (UsernameChanged e.NewTextValue))
                            View.Button(text = "Login", command = fun _ -> (loginUser dispatch))
                            ]))

module Chat =
    let prettyTimestamp (timestamp:DateTimeOffset) =
        timestamp.ToString("g", CultureInfo.CreateSpecificCulture("de-CH"))
        
    let chatBubble username (message: ChatMessage) =
        let bubble column color =
            View.ContentView
                (padding = Thickness(0., 8.),
                 content=
                    View.Frame
                        (cornerRadius=3.,
                         hasShadow=false,
                         backgroundColor= color,
                         padding = Thickness 0.,
                         isClippedToBounds=true,
                         margin = 
                             (if column = 1 then
                                Thickness (32., 0., 0., 0.)
                              else
                                Thickness (0., 0., 0., 32.)
                             ),
                         content = 
                            View.Grid
                                (rowdefs = [ Dimension.Star; Dimension.Auto ],
                                 margin = Thickness(4.,4.),
                                 children =
                                     [ View
                                         .Label(text = message.Message,
                                                textColor = Color.Black,
                                                fontSize = FontSize.fromNamedSize NamedSize.Default)
                                           .Row(0)
                                       View
                                           .Label(text = $"{message.Username} - {prettyTimestamp message.Timestamp}",
                                                  textColor = Color.Black,
                                                  fontSize = FontSize.fromNamedSize NamedSize.Small)
                                           .Row(1)])))

        if username = message.Username then bubble 1 Color.PowderBlue else bubble 0 Color.PaleGreen

    let chatMessageView model =
        model.Messages
        |> List.rev
        |> List.map (fun msg -> chatBubble model.Username msg)

    let view model dispatch =
        View.ContentPage
            (title = "Fabulous Chat",
             content =
                 View.Grid
                     (rowdefs = [ Dimension.Star; Dimension.Auto ],
                      margin= Thickness 8.,
                      children =
                          [ View
                              .CollectionView(items = (chatMessageView model))
                                .Row(0)
                            (View.Grid
                                (coldefs = [ Dimension.Star; Dimension.Auto ],
                                 children =
                                     [ (View.Entry
                                         (placeholder = "Speak your mind...",
                                          text = model.ChatMessage,
                                          keyboard=Keyboard.Chat,
                                          completed = (fun _ -> dispatch SendMessage),
                                          textChanged = fun e -> dispatch (MessageChanged e.NewTextValue)
                                          ))
                                         .Column(0)
                                       View
                                           .Button(text = "Send", command = fun () -> dispatch SendMessage)
                                           .Column(1) ]))
                                .Row(1) ]))

module App =
    let initModel =
        { Username = ""
          ChatMessage = ""
          Messages = []
          SignalRConnection = None
          CurrentPage = Login
          AppState = Busy }

    let init () = initModel, Cmd.none

    let sendMessage (connection: HubConnection) username chatMessage =
        let chatMessage =
            { Username = username
              Message = chatMessage
              Timestamp = DateTimeOffset.Now }

        async {
            do! (SignalR.sendMessage connection chatMessage)
            return MessageSent
        }
        |> Cmd.ofAsyncMsg

    let update msg (model: Model) =
        match msg with
        | LoggingIn -> { model with AppState = Busy }, Cmd.none
        | LoggedIn ->
            { model with
                  CurrentPage = Chat
                  AppState = Ready },
            Cmd.none
        | UsernameChanged username -> { model with Username = username }, Cmd.none
        | MessageReceived chatMessage ->
            { model with
                  Messages = chatMessage :: model.Messages },
            Cmd.none
        | MessageSent ->
            { model with
                  ChatMessage = ""
                  AppState = Ready },
            Cmd.none
        | MessageChanged newMessage -> { model with ChatMessage = newMessage }, Cmd.none
        | Connected connection ->
            { model with
                  SignalRConnection = Some connection
                  AppState = Ready },
            Cmd.none
        | SendMessage ->
            { model with AppState = Busy },
            match model.SignalRConnection with
            | Some (connection) -> (sendMessage connection model.Username model.ChatMessage)
            | None -> Cmd.none

    //    let ch model dispatch =
//        View.ContentPage()
    let view (model: Model) dispatch =
        // todo: add nav page
        // todo: set navpage content
        match model.CurrentPage with
        | Chat -> Chat.view model dispatch
        | Login -> Login.view model dispatch

    // Note, this declaration is needed if you enable LiveUpdate
    let program =
        XamarinFormsProgram.mkProgram init update view
#if DEBUG
        |> Program.withConsoleTrace
#endif

type App() as app =
    inherit Application()

    let runner =
        App.program |> XamarinFormsProgram.run app

#if DEBUG
    // Uncomment this line to enable live update in debug mode.
    // See https://fsprojects.github.io/Fabulous/Fabulous.XamarinForms/tools.html#live-update for further  instructions.
    //
    do runner.EnableLiveUpdate()
#endif

// Uncomment this code to save the application state to app.Properties using Newtonsoft.Json
// See https://fsprojects.github.io/Fabulous/Fabulous.XamarinForms/models.html#saving-application-state for further  instructions.
#if APPSAVE
    let modelId = "model"

    override __.OnSleep() =

        let json =
            Newtonsoft.Json.JsonConvert.SerializeObject(runner.CurrentModel)

        Console.WriteLine("OnSleep: saving model into app.Properties, json = {0}", json)
        app.Properties.[modelId] <- json

    override __.OnResume() =
        Console.WriteLine "OnResume: checking for model in app.Properties"

        try
            match app.Properties.TryGetValue modelId with
            | true, (:? string as json) ->

                Console.WriteLine("OnResume: restoring model from app.Properties, json = {0}", json)

                let model =
                    Newtonsoft.Json.JsonConvert.DeserializeObject<App.Model>(json)

                Console.WriteLine("OnResume: restoring model from app.Properties, model = {0}", (sprintf "%0A" model))
                runner.SetCurrentModel(model, Cmd.none)

            | _ -> ()
        with ex -> App.program.onError ("Error while restoring model found in app.Properties", ex)

    override this.OnStart() =
        Console.WriteLine "OnStart: using same logic as OnResume()"
        this.OnResume()
#endif
