Following are configuration parameters for this program:

Add RegistrationPrefix & DTDL modelId for the device in appsettings.json file as below
---------------------------------------------------------------------------
{
  "deviceId": "demodevice",
  "modelId": "dtmi:com:example:Thermostat;1",
  "GlobalDeviceEndpoint": "global.azure-devices-provisioning.net",
  "deviceSuffix": "1",
  "GatewayHostName": "<IoT Edge GatewayHost>",
  "EnrollmentType": "Group"
}

For local development & testing
Add this in secrets.json file 
  Right click project in Visual Studio > Manage User Secrets) OR
  Run "dotnet user-secrets" and use options to list, set, remove secrets  OR
  Use extension ".NET Core User Secrets" OR
  Copy below settings into appsettings.json
----------------------------------------
{
  "DpsIdScope": "<ID Scope>",
  "DPSPrimaryKey": "<Primary key"

 }

 
    Option 1: Copy secrets.json content into appsettings.json  OR
    Option 2: Configure them as Environment variables  OR
    Option 3: Pass them as command line parameters

How to run -

Set the parameters as above and run following 
./simdesk.exe 

Override any of the parameters above,e.global
./simdesk.exe GatewayHostName="raspberrypi.local"

Run multiple devices using powershell script as below -
 for($i=1;$i -le 5; $i++) {start pwsh "-command ./simdevice -args deviceSuffix=$i GatewayHostName=raspberrypi.local"}