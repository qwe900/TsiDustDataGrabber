namespace Program
{
    public class Device
    {
        public string account_id { get; set; }
        public string device_id { get; set; }
        public string model { get; set; }
        public string serial { get; set; }
        public Metadata metadata { get; set; }
        public string status { get; set; }
        public string created_at { get; set; }
        public string created_by { get; set; }
        public DateTime updated_at { get; set; }
        public string updated_by { get; set; }
        public DateTime date_last_data { get; set; }
        public int chartid { get; set; }

        public class Metadata
        {
            public string friendlyName { get; set; }
            public bool is_public { get; set; }
            public bool is_indoor { get; set; }
            public bool is_owned { get; set; }
            public double longitude { get; set; }
            public double latitude { get; set; }
        }
    }
}