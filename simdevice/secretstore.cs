using System;
using System.Collections.Generic;
using System.Text;
using NeoSmart.SecureStore;
using System.IO;


namespace simdevice
{
   public  class secretstore
    {

        private static secretstore _instance = null;

        private static Object _mutex = new object();

        private static Parameters _parameters;
        private  secretstore()
        {
          
        }

        public static secretstore GetInstance(Parameters parameters)
        {
             if (_instance==null)
            {
                lock (_mutex)
                {
                    if(_instance==null)
                    {
                        _parameters = parameters;
                        _instance = new secretstore();
                    }
                }

            }
            return _instance;

        }
        
        public static string GetSecret(string keyname)
        {
            if (!File.Exists("secrets.bin"))
            {  //secrets.bin file not found, so create it
                using (var sman = SecretsManager.CreateStore())
                {
                    //securely derive key from primary key
                    sman.LoadKeyFromPassword(_parameters.PrimaryKey);
                    // Export the keyfile for future use to retrive secret
                    sman.ExportKey("secrets.key");
                    //save store in a file
                    sman.SaveStore("secrets.bin");

                    return "";

                }
            }
            else
            {
                using (var sman = SecretsManager.LoadStore("secrets.bin"))
                {
                    // or use an existing key file:
                    sman.LoadKeyFromFile("secrets.key");
                    //save iotconnection string
                    return sman.Get(keyname);
                }
            }


        }
        public static  void SaveSecret(string keyname, string ssecret)
        {
            using (var sman = SecretsManager.LoadStore("secrets.bin"))
            {
                // or use an existing key file:
                sman.LoadKeyFromFile("secrets.key");
                //save secret  string
                sman.Set(keyname, ssecret);
                //save store in a file
                sman.SaveStore("secrets.bin");


            }


        }
    }
}
