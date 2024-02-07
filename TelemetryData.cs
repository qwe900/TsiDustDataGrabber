namespace TSItool
{
    public class TelemetryData
    {
        public string cloud_account_id { get; set; }
        public string cloud_device_id { get; set; }
        public double mcpm10 { get; set; }
        public double mcpm1x0 { get; set; }
        public double mcpm2x5 { get; set; }
        public double mcpm4x0 { get; set; }
        public double rh { get; set; }
        public double temperature { get; set; }
        public DateTime timestamp { get; set; }
    }
}