Priority is first commandline > environment > appsettings.json
Only parameters passed will get replaced or overridden

Passing parameters via appsettings.json
    Configure appsettings.json as per appsetting-example.json

Passing parameters via environment
    Configure parameters in following format in powershell __ for nesting and <number> for array position

    $Env:HybridConnectionServerHost__ForwardingRules__0__ServiceBusConnectionName="myname"
     or
    $Env:HybridConnectionServerHost:ForwardingRules:0:ServiceBusConnectionName="myname"

Passing parameters via command line
    Pass required parameters in following format  --<root>:<child>:<arrayindex>:<child>
    e.g 
    
    {
        "HybridConnectionServerHost": {
            "ServiceBusNamespace": "xyz.servicebus.windows.net",
            "ServiceBusKeyName": "RootManageSharedAccessKey",
            "ServiceBusKey": "",
            "ForwardingRules": [
                                    {
                                    "ServiceBusConnectionName": "hybridtwo",
                                    "TargetHostname": "localhost",
                                    "TargetPorts": "4243",
                                    "InstanceCount": 2
                                }
                                ]
            } 
        
    }
    
    for above configuration, pass commandline parameters as below
        dotnet run --HybridConnectionServerHost:ForwardingRules:0:ServiceBusConnectionName "trial"

