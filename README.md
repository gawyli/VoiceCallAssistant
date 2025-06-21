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
	
	```
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
9. Navigate to `appsettings.Development.json` and change Database Name to the name set in CosmosDb 
10. Go to

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