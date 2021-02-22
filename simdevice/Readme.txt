Following are configuration parameters for this program:

Add RegistrationPrefix & DTDL modelId for the device in appsettings.json file as below
---------------------------------------------------------------------------
{
  "RegistrationPrefix": "demodevice_",   
  "ModelId": "dtmi:com:example:Thermostat;1"
}

For local development & testing
Add this in secrets.json file (right click project > Manage User Secrets)
----------------------------------------
{
  "DpsIdScope": "",
  "DpsGroupPrimaryKey": "",
  "DpsGroupSecondaryKey": "",

  "FileUploadBlob": "",
  "FileDownloadBlob": "",

 }

 For production 
    Option 1: Copy secrets.json content into appsettings.json  OR
    Option 2: Configure them as Environment variables  OR
    Option 3: Pass them as command line parameters