# TSI Dust Data Grabber

TSI Tool is a designed to fetch telemetry data from TSI TSI BlueSky™ IoT devices. Utilizing a robust set of technologies including .NET Core, Entity Framework Core, MySQL, and RestSharp, it offers a seamless integration with IoT devices to monitor and analyze telemetry data in real-time.

## Features

- **Telemetry Data Fetching:** Asynchronously fetches telemetry data for devices, leveraging RestSharp for API calls.
- **Data Conversion:** Converts RFC3339 timestamps to local time for easier interpretation.
- **Database Management:** Utilizes MySQL for storing device telemetry data and metadata, with Entity Framework Core managing migrations and data access.
- **Authentication Management:** Automates the OAuth authentication process to secure API calls.
- **Real-Time Data Updates:** Periodically updates device lists and telemetry data using .NET Core's `System.Threading.Timer`.


## Prerequisites

Before you begin, ensure you have installed:
- .NET Core SDK (version specified in your project)
- MySQL Server
- An IDE such as Visual Studio or Visual Studio Code

## Getting Started

1. **Clone the repository**

    Set up your MySQL Database

Create your MySQL database and update the connection strings in appsettings.json accordingly.
you need a database for the Data and for the Configuration/Status values

    Configure appsettings.json

Ensure your appsettings.json file contains the necessary configurations for:

    MySQL connection strings (Datastorage_Database and Config_Database)
    API client settings (client api acces)

    Run the Application



The console will display logs regarding the fetching and insertion of telemetry data, including any errors encountered during the process.

For more detailed information about each component and how to customize the tool for your needs, refer to the inline comments within each source file.

