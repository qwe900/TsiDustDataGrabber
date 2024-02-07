using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Program;
using RestSharp;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TSItool
{
    internal class Program
    {
        private static string accessToken;
        private static IConfigurationRoot configuration;
        private static System.Threading.Timer deviceDataTimer;
        private static System.Threading.Timer deviceListTimer;
        private static List<Device> devices = new List<Device>();
        private static DateTime tokenExpiresAt;
        //converts RFC3339 to local time
        public static DateTime ConvertRfc3339ToLocalTime(DateTime rfc3339)
        {
            return rfc3339.ToLocalTime();
        }

        public static async Task<List<TelemetryData>> FetchTelemetryDataForDevice(Device device)
        {
            List<TelemetryData> telemetryDataList = new List<TelemetryData>();
            Console.WriteLine($"Fetching telemetry data for device {device.device_id}...");
            var endpoint = "telemetry/flat-format";
            var telemetryFields = new List<string> { "mcpm1x0", "mcpm2x5", "mcpm4x0", "mcpm10", "temperature", "rh" };

            var queryParams = new List<KeyValuePair<string, string>>
    {
        new KeyValuePair<string, string>("device_id", device.device_id),
        new KeyValuePair<string, string>("start_date", device.date_last_data.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ"))
    };

            // Adding telemetry fields to the query parameters
            foreach (var field in telemetryFields)
            {
                queryParams.Add(new KeyValuePair<string, string>("telem[]", field));
            }

            // Make the API call and process the response
            await MakeAuthenticatedApiCall(endpoint, queryParams, response =>
            {
                if (response.IsSuccessful)
                {
                    // Parse the response JSON and add telemetry data to the list
                    var responseData = JsonSerializer.Deserialize<List<TelemetryData>>(response.Content);
                    //Console.WriteLine(response.Content);
                    telemetryDataList.AddRange(responseData);

                    Console.WriteLine($"Data for Name: {device.metadata.friendlyName} , Device: {device.device_id} and Chartid: {device.chartid} fetched.");
                }
                else
                {
                    Console.WriteLine($"Failed to fetch telemetry data for device {device.device_id}. Error: {response.ErrorMessage}");
                }
            });

            return telemetryDataList;
        }

        public static async Task<List<TelemetryData>> FetchTelemetryDataForDevices(List<Device> devices)
        {
            List<TelemetryData> allTelemetryData = new List<TelemetryData>();

            foreach (var device in devices)
            {
                var telemetryData = await FetchTelemetryDataForDevice(device);
                InsertTelemetryData(telemetryData, device.chartid);
            }

            return allTelemetryData;
        }

        public static DateTime GetLastDatasetDateTime(int chartId, string connectionStringConfig)
        {
            DateTime lastDateTime = DateTime.Now.AddDays(-29);

            // SQL command to select the most recent datetime
            string commandText = $"SELECT `datetime` FROM `{chartId}` ORDER BY `datetime` DESC LIMIT 1";

            // Create and open a connection
            using (MySqlConnection connection = new MySqlConnection(connectionStringConfig))
            {
                try
                {
                    connection.Open();

                    // Execute the command
                    using (MySqlCommand cmd = new MySqlCommand(commandText, connection))
                    {
                        var result = cmd.ExecuteScalar(); // Executes query and returns the first column of the first row

                        if (result != DBNull.Value && result != null)
                        {
                            lastDateTime = Convert.ToDateTime(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred: {ex.Message}");
                    // Handle exceptions or log errors as needed
                }
            }

            return lastDateTime;
        }

        public static void InsertTelemetryData(List<TelemetryData> telemetryDataList, int chartid)
        {
            Console.Write("Inserting data to MySQL");
            string connectionString = configuration["ConnectionStrings:Datastorage_Database"];

            if (!CheckMySQLConnection(connectionString))
            {
                Console.WriteLine("MySQL connection failed. Data insertion aborted.");
                return;
            }

            if (telemetryDataList.Count == 0)
            {
                Console.WriteLine("Telemetry data list is empty. Data insertion aborted.");
                return;
            }

            try
            {
                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    if (!TableExists(connection, chartid))
                    {
                        Console.WriteLine($"Table {chartid} does not exist. Skip");
                        return;
                    }

                    using (var transaction = connection.BeginTransaction())
                    {
                        //ad values mcpm1x0, mcpm2x5, mcpm4x0, mcpm10, temperature, rh

                        var query = $"INSERT IGNORE INTO `{chartid}` (datetime, mcpm1x0, mcpm2x5, mcpm4x0, mcpm10, temperature, rh) VALUES (@Datetime, @Mcpm1x0, @Mcpm2x5, @Mcpm4x0, @Mcpm10, @Temperature, @Rh)";
                        using (var command = new MySqlCommand(query, connection))
                        {
                            command.Transaction = transaction;
                            int rowsInserted = 0; // Initialize a counter for the inserted rows

                            foreach (var data in telemetryDataList)
                            {
                                command.Parameters.Clear();
                                command.Parameters.AddWithValue("@Datetime", data.timestamp);
                                command.Parameters.AddWithValue("@Mcpm1x0", data.mcpm1x0);
                                command.Parameters.AddWithValue("@Mcpm2x5", data.mcpm2x5);
                                command.Parameters.AddWithValue("@Mcpm4x0", data.mcpm4x0);
                                command.Parameters.AddWithValue("@Mcpm10", data.mcpm10);
                                command.Parameters.AddWithValue("@Temperature", data.temperature);
                                command.Parameters.AddWithValue("@Rh", data.rh);

                                rowsInserted += command.ExecuteNonQuery(); // Accumulate the count of inserted rows
                            }

                            transaction.Commit();
                            Console.WriteLine($"Insertion successful: {rowsInserted} rows inserted."); // Report the total number of inserted rows
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                // Handle or log the exception as needed
            }
        }

        private static async Task AuthenticateAsync()
        {
            string url = $"{configuration["ApiClient:BaseUrl"]}/oauth/client_credential/accesstoken?grant_type=client_credentials";

            // Create RestSharp client and POST request object
            var client = new RestClient(url);
            var request = new RestRequest();
            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddParameter("client_id", configuration["ApiClient:ClientId"]);
            request.AddParameter("client_secret", configuration["ApiClient:ClientSecret"]);
            request.AddParameter("scope", configuration["ApiClient:Scope"]);

            var response = client.Post(request);

            if (response.IsSuccessful)
            {
                // Parse the JSON response
                var jsonResponse = JsonNode.Parse(response.Content);
                accessToken = jsonResponse["access_token"].ToString();
                var expiresIn = int.Parse(jsonResponse["expires_in"].ToString());
                var issuedAt = DateTime.UtcNow; // Assuming the token is issued at the time of this response
                tokenExpiresAt = issuedAt.AddSeconds(expiresIn);

                Console.WriteLine($"Access Token: {accessToken}");
                Console.WriteLine($"Token Expires At: {tokenExpiresAt}");
                SaveTokenToConfiguration(accessToken, issuedAt);
            }
            else
            {
                // Handle error
                Console.WriteLine("Failed to authenticate.");
                Console.WriteLine($"Status Code: {response.StatusCode}");
                Console.WriteLine($"Error Message: {response.ErrorMessage}");
            }
        }

        // Synchronous version of CheckMySQLConnection
        private static bool CheckMySQLConnection(string connectionString)
        {
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open(); // Attempt to open the connection synchronously
                    return true; // Connection was successfully opened
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to the database. Error: {ex.Message}");
                return false; // Connection failed
            }
        }

        private static void DisplayDevicesTable(List<Device> deviceList)
        {
            // Determine the maximum length of each column
            int maxDeviceIdLength = Math.Max("Device ID".Length, deviceList.Max(device => device.device_id.Length)) + 2;
            int maxFriendlyNameLength = Math.Max("Friendly Name".Length, deviceList.Max(device => device.metadata.friendlyName.Length)) + 2;
            int maxModelLength = Math.Max("Model".Length, deviceList.Max(device => device.model.Length)) + 2;
            int maxSerialLength = Math.Max("Serial".Length, deviceList.Max(device => device.serial.Length)) + 2;
            int updatedAtLength = "Updated At     ".Length + 8; // Fixed format length
            int lastDataLength = "Last Data     ".Length + 8; // Fixed format length
            int chartIdLength = "Chartid".Length + 2;

            // Header
            Console.WriteLine($"{"Device ID".PadRight(maxDeviceIdLength)}{"Friendly Name".PadRight(maxFriendlyNameLength)}{"Model".PadRight(maxModelLength)}{"Serial".PadRight(maxSerialLength)}{"Updated At".PadRight(updatedAtLength)}{"Last Data".PadRight(lastDataLength)}{"Chartid".PadRight(chartIdLength)}");

            // Separator
            Console.WriteLine(new string('-', maxDeviceIdLength + maxFriendlyNameLength + maxModelLength + maxSerialLength + updatedAtLength + lastDataLength + chartIdLength));

            // Rows
            foreach (var device in deviceList)
            {
                // Convert updatedAtString and lastDataString from RFC 3339 to local time
                string updatedAtString = ConvertRfc3339ToLocalTime(device.updated_at).ToString("yyyy-MM-dd HH:mm:ss");
                string lastDataString = ConvertRfc3339ToLocalTime(device.date_last_data).ToString("yyyy-MM-dd HH:mm:ss");

                Console.WriteLine($"{device.device_id.PadRight(maxDeviceIdLength)}{device.metadata.friendlyName.PadRight(maxFriendlyNameLength)}{device.model.PadRight(maxModelLength)}{device.serial.PadRight(maxSerialLength)}{updatedAtString.PadRight(updatedAtLength)}{lastDataString.PadRight(lastDataLength)}{device.chartid.ToString().PadRight(chartIdLength)}");
            }
        }

        private static async void EnsureTokenIsValid()
        {
            if (!IsTokenValid())
            {
                Console.WriteLine("Access token is invalid or expired. Re-authenticating...");
                await AuthenticateAsync();
            }
        }

        private static async Task FetchDeviceList()
        {
            await MakeAuthenticatedApiCall("devices", new List<KeyValuePair<string, string>>(), response =>
            {
                if (response.IsSuccessful)
                {
                    // Parse the JSON response and update the devices list
                    devices = System.Text.Json.JsonSerializer.Deserialize<List<Device>>(response.Content);
                    //Console.WriteLine(devices.ToString());
                    GetChartID();
                    DisplayDevicesTable(devices);

                    FetchTelemetryDataForDevices(devices);
                }
                else
                {
                    Console.WriteLine("Failed to fetch device list.");
                }
            });
        }

        private static void GetChartID()
        {
            string connectionStringData = configuration["ConnectionStrings:Datastorage_Database"];
            string connectionStringConfig = configuration["ConnectionStrings:Config_Database"];
            if (!CheckMySQLConnection(connectionStringConfig) || !CheckMySQLConnection(connectionStringData))
            {
                Console.WriteLine("MySQL connection failed. Data insertion aborted.");
                return;
            }

            using (MySqlConnection configConnection = new MySqlConnection(connectionStringConfig))
            {
                configConnection.Open(); // Open connection to Config Database
                foreach (var device in devices)
                {
                    try
                    {
                        using (MySqlCommand cmd = new MySqlCommand())
                        {
                            cmd.Connection = configConnection;
                            cmd.CommandText = "SELECT chartid FROM tsi_dust WHERE serialnumber = @Serial AND name = @Name";
                            cmd.Parameters.AddWithValue("@Serial", device.serial);
                            cmd.Parameters.AddWithValue("@Name", device.metadata.friendlyName);

                            var result = cmd.ExecuteScalar();
                            if (result != null)
                            {
                                int chartid = Convert.ToInt32(result);
                                device.chartid = chartid;
                                //Console.WriteLine($"Chart ID {device.chartid} found for device {device.serial}.");

                                device.date_last_data = GetLastDatasetDateTime(device.chartid, connectionStringData);
                                //Console.WriteLine($"Last data for device {device.serial} is {device.date_last_data}.");
                                UpdateLastDataset(chartid, ConvertRfc3339ToLocalTime(GetLastDatasetDateTime(chartid, connectionStringData)));
                            }
                            else
                            {
                                // Insert and then create table as chart ID does not exist
                                cmd.Parameters.Clear();

                                //add a daytime value 29 before the current date
                                DateTime date = DateTime.UtcNow.AddDays(-29);
                                string dateStr = date.ToString("yyyy-MM-dd HH:mm:ss");

                                cmd.CommandText = "INSERT INTO tsi_dust (`serialnumber`, `name`, `created`, `active`, `lat`, `long`,`lastdataset`) VALUES (@Serial, @Name, Now(), 1, @Lat, @Long, @dateStr)";
                                cmd.Parameters.AddWithValue("@Serial", device.serial);
                                cmd.Parameters.AddWithValue("@Name", device.metadata.friendlyName);
                                cmd.Parameters.AddWithValue("@Lat", device.metadata.latitude);
                                cmd.Parameters.AddWithValue("@Long", device.metadata.longitude);
                                cmd.Parameters.AddWithValue("@dateStr", dateStr);

                                cmd.ExecuteNonQuery();
                                long lastInsertedId = cmd.LastInsertedId;
                                device.chartid = Convert.ToInt32(lastInsertedId);
                                device.date_last_data = date;

                                // Create chart table in the Data Storage Database
                                using (MySqlConnection dataConnection = new MySqlConnection(connectionStringData))
                                {
                                    dataConnection.Open(); // Open connection to Data Storage Database

                                    using (MySqlCommand dataCmd = new MySqlCommand())
                                    {
                                        dataCmd.Connection = dataConnection;
                                        dataCmd.CommandText = $@"

                                CREATE TABLE IF NOT EXISTS `{device.chartid}` (
                                    `datetime` datetime NOT NULL,
                                    `mcpm1x0` float(5, 3) NULL DEFAULT NULL,
                                    `mcpm2x5` float(5, 3) NULL DEFAULT NULL,
                                    `mcpm4x0` float(5, 3) NULL DEFAULT NULL,
                                    `mcpm10` float(5, 3) NULL DEFAULT NULL,
                                    `temperature` float(5, 3) NULL DEFAULT NULL,
                                    `rh` float(5, 3) NULL DEFAULT NULL,
                                    PRIMARY KEY (`datetime`) USING BTREE,
                                    INDEX `h`(`datetime`) USING BTREE
                                        ) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;";
                                        dataCmd.ExecuteNonQuery();
                                        Console.WriteLine($"Table {device.chartid} created for device {device.serial}.");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"An error occurred processing device {device.serial}: {ex.Message}");
                    }
                }
            }
        }

        private static void InitializeTimers()
        {
            // start the timer for periodic updates

            var autoEvent = new AutoResetEvent(false);

            deviceListTimer = new System.Threading.Timer(e => FetchDeviceList(), autoEvent, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        }

        private static bool IsTokenValid()
        {
            return !string.IsNullOrEmpty(accessToken) && DateTime.UtcNow < tokenExpiresAt;
        }

        private static void LoadConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            configuration = builder.Build();
        }

        private static void LoadTokenFromConfiguration()
        {
            accessToken = configuration["AuthToken:AccessToken"];
            var expiresAtString = configuration["AuthToken:ExpiresAt"];
            if (!string.IsNullOrEmpty(expiresAtString))
            {
                tokenExpiresAt = DateTime.Parse(expiresAtString);
            }
        }

        private static async Task Main(string[] args)
        {
            LoadConfiguration();
            await AuthenticateAsync();
            InitializeTimers();

            Console.ReadLine();
        }
        private static async Task MakeAuthenticatedApiCall(string resourceEndpoint, List<KeyValuePair<string, string>> queryParams, Action<IRestResponse> handleResponse)
        {
            Console.WriteLine($"Making API call to {resourceEndpoint}...");
            EnsureTokenIsValid(); // Ensure we have a valid token

            string apiUrl = $"{configuration["ApiClient:BaseUrl"]}/{resourceEndpoint}";
            // Console.WriteLine($"API URL: {apiUrl}");
            var client = new RestClient(apiUrl);
            var request = new RestRequest();
            request.AddHeader("Authorization", $"Bearer {accessToken}"); // Use the access token
            request.AddHeader("Accept", "application/json");

            // Add query parameters to the request if any
            if (queryParams != null)
            {
                foreach (var param in queryParams)
                {
                    request.AddParameter(param.Key, param.Value, ParameterType.QueryString);
                }
            }

            // Execute the request asynchronously and handle the response
            var response = await client.ExecuteAsync(request);
            //Console.WriteLine(response.Content);
            handleResponse(response);
        }

        private static void SaveTokenToConfiguration(string token, DateTime expiresAt)
        {
            // Convert expiration DateTime to a string or a timestamp as preferred
            string expiresAtString = expiresAt.ToString("o"); // ISO 8601 format

            // Update in-memory configuration
            configuration["AuthToken:AccessToken"] = token;
            configuration["AuthToken:ExpiresAt"] = expiresAtString;

            // Persist changes to the appsettings.json or a separate config file
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var jsonConfig = File.ReadAllText(filePath);
            dynamic jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonConfig);
            jsonObj["AuthToken"]["AccessToken"] = token;
            jsonObj["AuthToken"]["ExpiresAt"] = expiresAtString;
            string output = Newtonsoft.Json.JsonConvert.SerializeObject(jsonObj, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(filePath, output);
        }
        private static bool TableExists(MySqlConnection connection, int chartid)
        {
            using (MySqlCommand cmd = new MySqlCommand())
            {
                cmd.Connection = connection;
                cmd.CommandText = $"SHOW TABLES LIKE '{chartid}'";
                return cmd.ExecuteScalar() != null;
            }
        }
        //create a function which updates lastdataset in the config database
        private static void UpdateLastDataset(int chartid, DateTime lastDataset)
        {
            string connectionString = configuration["ConnectionStrings:Config_Database"];
            if (!CheckMySQLConnection(connectionString))
            {
                Console.WriteLine("MySQL connection failed. Data insertion aborted.");
                return;
            }

            try
            {
                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (MySqlCommand cmd = new MySqlCommand())
                    {
                        cmd.Connection = connection;
                        cmd.CommandText = "UPDATE tsi_dust SET lastdataset = @LastDataset, updated = @Updated WHERE chartid = @Chartid";
                        cmd.Parameters.AddWithValue("@LastDataset", lastDataset);
                        cmd.Parameters.AddWithValue("@Updated", DateTime.Now);
                        cmd.Parameters.AddWithValue("@Chartid", chartid);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                // Handle or log the exception as needed
            }
        }
    }
}