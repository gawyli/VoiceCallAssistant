# Project Title

Voice Call Assistant

## Getting Started

These instructions will give you a copy of the project up and running on
your local machine for development and testing purposes. See deployment
for notes on deploying the project on a live system.

### Prerequisites

- OpenAI Api Key
- CosmosDb Connection String
	- Created Database 
	- Created a Routines container with PartitionKey set to `/Username`
- Application Insights Connection String
- Twilio Account, Phone Number and Authentication Token

### Installation
Twilio requres public address for webhook callback. To expose an application to the internet we can use `github codespaces` or `ngrok`. 

#### Option 1. Github Codespaces

1. Fork the repository.
2. Create github codespace for the repository.
	- Press a green button "<> Code" next to about section.
	- Select Codespaces.
	- Click "Create codespace on <branch name>".
3. Visual Studio Code will open in a new tab at the browser.
4. Install "C# Dev Kit" extension.
5. Install "JSON Server" extension.
6. Once extentions are installed, close and open codespace (Visual Studio Code)
7. Once codespace is reopened, navigate in the solution explorer to
	- `VoiceCallAssistant/VoiceCallAssistant.csproj`
	- Right click on the file
	- From the Right-Click Menu click on "Manage User Secrets"
	- Copy and paste configuration secrets into the "secrets.json"
	
	```json
	{
		"TwilioService": {
			"AccountSid": "xxx",
			"AuthToken": "xxx",
			"CallerId": "+44xxx",
			"WebhookHost": "xxx.app.github.dev"
		},
		"OpenAI": {
			"ApiKey": "sk-proj-xxx"
		},
		"Database": {
			"ConnectionString": "AccountEndpoint=InstrumentationKey=xxx"
		},
		"AzureMonitor": {
			"ConnectionString": "InstrumentationKey=xxx"
		}
	}
	```
8. Replace "xxx" with your secrets.
	- For the WebhookHost you have to paste the codespace URL with the port 5055.
	- Your URL should look like this: `abc-abc-abc-5055.app.github.dev`, notice port 5055 before `.app.github.dev`
9. Navigate to `appsettings.Development.json` and change Database Name to the database name set in CosmosDb 
10. Open Run and Debug tab on left side, click the green button then from dropdown list select `C#` and select `C#: VoiceCallAssistant [http]`
	- It is important to have a code file opened e.g. `Program.cs` and then press the `Run and Debug` button
11. Wait until application opens. Once everything is set up, navigate in the terminal to the `Ports` tab (Should be little blue circle with a number in it). In the ports tab, you should see a one record with the `5055` port and your github codespaces address (It's your public address `This public address is used for WebhookHost in the appsettings. The address includes port`).
12. Right click on the `Private` value in the Visibility column and in the menu hover over `Port Visibility` and select `Public`.
13. Copy your public address and navigate to `https://your-github-codespaces-public-address-5055.app.github.dev/swagger/index.html`
14. In the swagger use `/api/routine/create` to create routine
	```json
	{
	"username": "TestUsername",
	"name": "Tesths",
	"scheduledTime": "08:00",
	"isMonFri": true,
	"phoneNumber": "+441934877388",
	"preferences": {
		"topicOfInterest": "Not in use",
		"toDos": "Not in use",
		"personalisedPrompt": "Welcome me with a morning affirmation for better day"
		}
	}
	```
15. Once you created routine, copy the `Id` from the bottom of the response body.
16. Navigate to `/api/call/request` endpoint and use the copied `Id` for `RoutineId` to request a call. 
17. You should receive a mobile call on the specified number.

#### Option 2. ngrok

1. Clone repository
```
git clone https://github.com/gawyli/VoiceCallAssistant.git
```

## Usage


```
To be continued 
```

## Features