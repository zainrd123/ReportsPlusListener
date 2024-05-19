using LSPD_First_Response.Engine.Scripting;
using LSPD_First_Response.Engine.Scripting.Entities;
using LSPD_First_Response.Mod.API;
using LSPD_First_Response.Mod.Callouts;
using Rage;
using StopThePed.API;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ReportsPlus
{
    public class Main : Plugin
    {
        // Vars
        private static readonly string DataPath = "ReportsPlus\\data";
        private static XDocument currentIDXDoc;
        private static XDocument calloutXDoc;
        private GameFiber dataCollection;
        internal static bool CurrentlyOnDuty;
        internal static Ped Player => Game.LocalPlayer.Character;
        private static Dictionary<LHandle, string> calloutDir = new Dictionary<LHandle, string>();



        // Plugin Checks
        private bool CheckPlugins()
        {
            bool hasCalloutInterface = IsPluginInstalled("CalloutInterface");
            bool hasStopThePed = IsPluginInstalled("StopThePed");

            return hasCalloutInterface && hasStopThePed;
        }

        private bool IsPluginInstalled(string pluginName)
        {
            return LSPD_First_Response.Mod.API.Functions.GetAllUserPlugins().Any(x => x.GetName().Name.Equals(pluginName));
        }


        // Init
        public override void Initialize()
        {
            calloutDir.Clear();
            if (!Directory.Exists(DataPath))
                Directory.CreateDirectory(DataPath);

            currentIDXDoc = new XDocument(new XElement("IDs"));
            LoadCurrentIDXDocument();
            LoadCalloutXDocument();

            // Check for required plugins
            bool pluginsInstalled = CheckPlugins();
            if (!pluginsInstalled)
            {
                Game.DisplayNotification("~r~ReportsPlus requires CalloutInterface.dll and StopThePed.dll to be installed.");
                Game.LogTrivial("ReportsPlus requires CalloutInterface.dll and StopThePed.dll to be installed.------------------------------------------------------------------------------------------");
                return; // Exit initialization if plugins are missing
            }

            LSPD_First_Response.Mod.API.Functions.OnOnDutyStateChanged += OnOnDutyStateChangedHandler;
            Game.LogTrivial("ReportsPlus Listener Plugin initialized.------------------------------------------------------------------------------------------");
        }



        // Loaders
        private void LoadCurrentIDXDocument()
        {
            string filePath = Path.Combine(DataPath, "currentID.xml");
            if (File.Exists(filePath))
            {
                currentIDXDoc = XDocument.Load(filePath);
            }
        }
        private void LoadCalloutXDocument()
        {
            string filePath = Path.Combine(DataPath, "callout.xml");
            if (File.Exists(filePath))
            {
                calloutXDoc = XDocument.Load(filePath);
            }
            else
            {
                calloutXDoc = new XDocument(new XElement("Callouts"));
                calloutXDoc.Save(filePath);
            }
        }



        // Establish Events
        private void SetupEventHandlers()
        {
            StopThePed.API.Events.askIdEvent += AskIDEvent;
            StopThePed.API.Events.pedArrestedEvent += PedArrestedEvent;
            StopThePed.API.Events.patDownPedEvent += PatDownPedEvent;
            StopThePed.API.Events.askDriverLicenseEvent += AskLicenseEvent;
            StopThePed.API.Events.askPassengerIdEvent += AskPassengerIDEvent;
            StopThePed.API.Events.stopPedEvent += STPStopPedEvent;
        }
        private static void CICalloutEvents()
        {
            LSPD_First_Response.Mod.API.Events.OnCalloutDisplayed += CalloutDisplayedEvent;

            void CalloutDisplayedEvent(LHandle handle)
            {
                Game.LogTrivial("ReportsPlus: Displaying Callout");
                Callout callout = CalloutInterface.API.Functions.GetCalloutFromHandle(handle);
                string calloutId = GenerateCalloutId();

                string agency = LSPD_First_Response.Mod.API.Functions.GetCurrentAgencyScriptName();
                string priority = "default";
                string description = "";
                string name = callout.FriendlyName;

                if (callout.ScriptInfo is CalloutInterfaceAPI.CalloutInterfaceAttribute calloutInterfaceInfo)
                {
                    agency = calloutInterfaceInfo.Agency.Length > 0 ? calloutInterfaceInfo.Agency : agency;
                    priority = calloutInterfaceInfo.Priority.Length > 0 ? calloutInterfaceInfo.Priority : "default";
                    description = calloutInterfaceInfo.Description;
                    name = calloutInterfaceInfo.Name;
                }

                string street = World.GetStreetName(World.GetStreetHash(callout.CalloutPosition));
                WorldZone zone = LSPD_First_Response.Mod.API.Functions.GetZoneAtPosition(callout.CalloutPosition);
                string currentTime = DateTime.Now.ToString("h:mm:ss tt");
                string currentDate = DateTime.Now.ToString("yyyy-MM-dd");

                // Clear existing callouts before adding new one
                calloutXDoc.Root.Elements("Callout").Remove();

                XElement calloutElements = new XElement("Callout",
                    new XElement("Number", calloutId),
                    new XElement("Type", callout.CalloutMessage),
                    new XElement("Description", description),
                    new XElement("Priority", priority),
                    new XElement("Street", street),
                    new XElement("Area", zone.RealAreaName),
                    new XElement("County", zone.County),
                    new XElement("StartTime", currentTime),
                    new XElement("StartDate", currentDate)
                );

                calloutXDoc.Root.Add(calloutElements);
                calloutXDoc.Save(Path.Combine(DataPath, "callout.xml"));
                Game.LogTrivial($"ReportsPlus: Callout {calloutId} data updated and displayed.");
            }
        }



        // Updates
        internal static void UpdateCalloutDataDoc(string calloutId, string key, string value)
        {

            // Load the XML file if not already loaded or if it might have changed
            calloutXDoc = XDocument.Load(Path.Combine(DataPath, "callout.xml"));

            // Find the callout with the specified ID
            XElement calloutElement = calloutXDoc.Descendants("Callout")
                                                .FirstOrDefault(c => c.Element("ID")?.Value == calloutId);

            if (calloutElement != null)
            {
                // Find the element to update
                XElement elementToUpdate = calloutElement.Element(key);
                if (elementToUpdate != null)
                {
                    // Update the element's value
                    elementToUpdate.Value = value;
                    Game.LogTrivial($"ReportsPlus: Updated {key} for callout ID {calloutId} to {value}");

                    // Save the changes back to the XML file
                    calloutXDoc.Save(Path.Combine(DataPath, "callout.xml"));
                }
                else
                {
                    // Key does not exist, so add it
                    calloutElement.Add(new XElement(key, value));
                    Game.LogTrivial($"ReportsPlus: Added {key} for callout ID {calloutId} with value {value}");
                    calloutXDoc.Save(Path.Combine(DataPath, "callout.xml"));
                }
            }
            else
            {
                Game.LogTrivial("ReportsPlus: No callout found with the specified ID");
            }
        }
        private void OnOnDutyStateChangedHandler(bool onDuty)
        {
            CurrentlyOnDuty = onDuty;
            if (onDuty)
            {
                GameFiber.StartNew(UpdateInt);
                SetupEventHandlers();
                CICalloutEvents();
                RefreshNearbyPeds();
                RefreshNearbyVehicles();
                Game.DisplayNotification("ReportsPlus Listener loaded successfully.------------------------------------------------------------------------------------------");
            }
        }
        private static void UpdateCurrentIDDoc(Ped ped)
        {

            if (!ped.Exists())
                return;

            var persona = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(ped);
            var existingEntry = currentIDXDoc.Descendants("ID").FirstOrDefault(e => e.Element("Name")?.Value == persona.FullName);
            if (existingEntry != null)
                return;

            int index = ped.IsInAnyVehicle(false) ? ped.SeatIndex + 2 : 0;
            XElement newEntry = new XElement("ID",
                new XElement("Name", persona.FullName),
                new XElement("Birthday", $"{persona.Birthday.Month}/{persona.Birthday.Day}/{persona.Birthday.Year}"),
                new XElement("Gender", persona.Gender),
                new XElement("Index", index)
            );

            currentIDXDoc.Root.Add(newEntry);
            currentIDXDoc.Save(Path.Combine(DataPath, "currentID.xml"));
            Game.LogTrivial("ReportsPlus: Updated currentID.data");
        }



        // Events
        private static void AskIDEvent(Ped ped)
        {
            AddPed(ped);
            UpdateCurrentIDDoc(ped);
        }
        private static void PedArrestedEvent(Ped ped)
        {
            AddPed(ped);
        }
        private static void PatDownPedEvent(Ped ped)
        {
            AddPed(ped);
            UpdateCurrentIDDoc(ped);
        }
        private static void AskLicenseEvent(Ped ped)
        {
            AddPed(ped);
            UpdateCurrentIDDoc(ped);
        }
        private static void AskPassengerIDEvent(Vehicle vehicle)
        {
            Ped[] passengers = vehicle.Passengers;
            for (int i = 0; i < passengers.Length; i++)
            {
                UpdateCurrentIDDoc(passengers[i]);
            }
        }
        private static void STPStopPedEvent(Ped ped)
        {
            AddPed(ped);
        }



        // Fetch Data
        private static string FetchVehicleRegistration(Vehicle vehicle)
        {
            switch (StopThePed.API.Functions.getVehicleRegistrationStatus(vehicle))
            {
                case STPVehicleStatus.Expired:
                    return "Expired";
                case STPVehicleStatus.None:
                    return "None";
                case STPVehicleStatus.Valid:
                    return "Valid";
            }
            return "";
        }
        private static string FetchVehicleInsurance(Vehicle vehicle)
        {
            switch (StopThePed.API.Functions.getVehicleInsuranceStatus(vehicle))
            {
                case STPVehicleStatus.Expired:
                    return "Expired";
                case STPVehicleStatus.None:
                    return "None";
                case STPVehicleStatus.Valid:
                    return "Valid";
            }
            return "";
        }
        private static string FetchVehicleData(Vehicle vehicle)
        {
            string driverName = vehicle.Driver.Exists() ? LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(vehicle.Driver).FullName : "";
            string colorCode = Rage.Native.NativeFunction.Natives.GET_VEHICLE_LIVERY<int>(vehicle) != -1 ? "" : $"{vehicle.PrimaryColor.R}-{vehicle.PrimaryColor.G}-{vehicle.PrimaryColor.B}";
            return $"licensePlate={vehicle.LicensePlate}&model={vehicle.Model.Name}&isStolen={vehicle.IsStolen}&isPolice={vehicle.IsPoliceVehicle}&owner={LSPD_First_Response.Mod.API.Functions.GetVehicleOwnerName(vehicle)}&driver={driverName}&registration={FetchVehicleRegistration(vehicle)}&insurance={FetchVehicleInsurance(vehicle)}&color={colorCode}";
        }
        private static string FetchPedData(Ped ped)
        {
            Persona personaDetails = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(ped);
            string birthDate = $"{personaDetails.Birthday.Month}/{personaDetails.Birthday.Day}/{personaDetails.Birthday.Year}";
            return $"name={personaDetails.FullName}&birthday={birthDate}&gender={personaDetails.Gender}&isWanted={personaDetails.Wanted}&licenseStatus={personaDetails.ELicenseState}&relationshipGroup={ped.RelationshipGroup.Name}";
        }



        // Update Methods
        private static void RefreshNearbyVehicles()
        {
            if (!Player.Exists())
            {
                Game.LogTrivial("ReportsPlus: Failed to update worldCars.data; Invalid Player");
                return;
            }
            Vehicle[] nearbyCars = Player.GetNearbyVehicles(15);
            string[] carsDataArray = new string[nearbyCars.Length];

            for (int i = 0; i < nearbyCars.Length; i++)
            {
                Vehicle vehicle = nearbyCars[i];
                if (vehicle.Exists())
                {
                    carsDataArray[Array.IndexOf(nearbyCars, vehicle)] = FetchVehicleData(vehicle);
                }
            }
            File.WriteAllText($"{DataPath}/worldCars.data", string.Join(",", carsDataArray));
            Game.LogTrivial("ReportsPlus: Updated worldCars.data");
        }
        private static void RefreshNearbyPeds()
        {
            if (!Player.Exists())
            {
                Game.LogTrivial("ReportsPlus: Failed to update worldPeds.data; Player is invalid");
                return;
            }
            Ped[] nearbyPeds = Player.GetNearbyPeds(15);
            string[] pedsDataArray = new string[nearbyPeds.Length];

            for (int i = 0; i < nearbyPeds.Length; i++)
            {
                Ped ped = nearbyPeds[i];
                if (ped.Exists())
                {
                    pedsDataArray[Array.IndexOf(nearbyPeds, ped)] = FetchPedData(ped);
                }
            }

            File.WriteAllText($"{DataPath}/worldPeds.data", string.Join(",", pedsDataArray));

            Game.LogTrivial("ReportsPlus: Updated worldPeds.data");
        }



        // Util
        private int CalculatePedAge(DateTime birthDate)
        {
            DateTime today = DateTime.Today;
            int age = today.Year - birthDate.Year;
            if (birthDate > today.AddYears(-age)) age--;
            return age;
        }
        private static string GenerateCalloutId()
        {
            return new Random().Next(10000, 100000).ToString();
        }
        private static void AddPed(Ped ped)
        {
            if (ped.Exists())
            {
                string data = FetchPedData(ped);
                string oldFile = File.ReadAllText($"{DataPath}/worldPeds.data");
                if (oldFile.Contains(LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(ped).FullName)) return;

                string addComma = oldFile.Length > 0 ? "," : "";

                File.WriteAllText($"{DataPath}/worldPeds.data", $"{oldFile}{addComma}{data}");
            }
        }
        private static void UpdateInt()
        {
            while (CurrentlyOnDuty)
            {
                RefreshNearbyPeds();
                RefreshNearbyVehicles();
                GameFiber.Wait(15000);
            }
        }



        // Finally
        public override void Finally()
        {
            LSPD_First_Response.Mod.API.Functions.OnOnDutyStateChanged -= OnOnDutyStateChangedHandler;
            if (dataCollection != null && dataCollection.IsAlive)
                dataCollection.Abort();

            currentIDXDoc.Save(Path.Combine(DataPath, "currentID.xml"));
            calloutXDoc.Save(Path.Combine(DataPath, "callout.xml"));
            Game.LogTrivial("ReportsPlus Listener cleaned up.");
        }
    }
}
