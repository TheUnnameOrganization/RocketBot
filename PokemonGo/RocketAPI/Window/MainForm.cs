using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using AllEnum;
using PokemonGo.RocketAPI.Enums;
using PokemonGo.RocketAPI.Exceptions;
using PokemonGo.RocketAPI.Extensions;
using PokemonGo.RocketAPI.GeneratedCode;
using System.Configuration;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using GMap.NET.WindowsForms.ToolTips;
using System.Threading;
using BrightIdeasSoftware;
using PokemonGo.RocketAPI.Helpers;

namespace PokemonGo.RocketAPI.Window
{
    public partial class MainForm : Form
    {
        public static MainForm Instance;
        public static SynchronizationContext synchronizationContext;

        GMapOverlay searchAreaOverlay = new GMapOverlay("areas");
        GMapOverlay pokestopsOverlay = new GMapOverlay("pokestops");
        GMapOverlay pokemonsOverlay = new GMapOverlay("pokemons");
        GMapOverlay playerOverlay = new GMapOverlay("players");

        GMarkerGoogle playerMarker;

        IEnumerable<FortData> pokeStops;
        IEnumerable<WildPokemon> wildPokemons;

        public MainForm()
        {
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(Settings.Instance.Language == "System" ? CultureInfo.InstalledUICulture.Name : Settings.Instance.Language);
            Thread.CurrentThread.CurrentCulture =  new CultureInfo(Settings.Instance.Language == "System" ? CultureInfo.InstalledUICulture.Name : Settings.Instance.Language);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(Settings.Instance.Language == "System" ? CultureInfo.InstalledUICulture.Name : Settings.Instance.Language);
            MessageBox.Show(CultureInfo.DefaultThreadCurrentCulture.Name + " " + Thread.CurrentThread.CurrentCulture.Name + " " + Thread.CurrentThread.CurrentUICulture.Name);
            synchronizationContext = SynchronizationContext.Current;
            InitializeComponent();
            ClientSettings = Settings.Instance;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            gMapControl1.MapProvider = GoogleMapProvider.Instance;
            gMapControl1.Manager.Mode = AccessMode.ServerOnly;
            GMapProvider.WebProxy = null;
            gMapControl1.Position = new PointLatLng(ClientSettings.DefaultLatitude, ClientSettings.DefaultLongitude);
            gMapControl1.DragButton = MouseButtons.Left;

            gMapControl1.MinZoom = 1;
            gMapControl1.MaxZoom = 20;
            gMapControl1.Zoom = 15;

            gMapControl1.Overlays.Add(searchAreaOverlay);
            gMapControl1.Overlays.Add(pokestopsOverlay);
            gMapControl1.Overlays.Add(pokemonsOverlay);
            gMapControl1.Overlays.Add(playerOverlay);

            playerMarker = new GMarkerGoogle(new PointLatLng(ClientSettings.DefaultLatitude, ClientSettings.DefaultLongitude),
                GMarkerGoogleType.orange_small);
            playerOverlay.Markers.Add(playerMarker);

            InitializeMap();
            InitializePokemonForm();
        }

        public void Restart()
        {
            InitializeMap();
            InitializePokemonForm();
        }

        private void InitializeMap()
        {
            playerMarker.Position = new PointLatLng(ClientSettings.DefaultLatitude, ClientSettings.DefaultLongitude);

            searchAreaOverlay.Polygons.Clear();
            S2GMapDrawer.DrawS2Cells(S2Helper.GetNearbyCellIds(ClientSettings.DefaultLongitude, ClientSettings.DefaultLatitude), searchAreaOverlay);
        }

        public static ISettings ClientSettings;
        private static int Currentlevel = -1;
        private static int TotalExperience = 0;
        private static int TotalPokemon = 0;
        private static bool Stopping = false;
        private static bool ForceUnbanning = false;
        private static bool FarmingStops = false;
        private static bool FarmingPokemons = false;
        private static DateTime TimeStarted = DateTime.Now;
        public static DateTime InitSessionDateTime = DateTime.Now;

        Client client;
        Client client2;
        LocationManager locationManager;

        public static double GetRuntime()
        {
            return ((DateTime.Now - TimeStarted).TotalSeconds) / 3600;
        }

        public void CheckVersion()
        {
            
            try
            {
                var match =
                    new Regex(
                        @"\[assembly\: AssemblyVersion\(""(\d{1,})\.(\d{1,})\.(\d{1,})\.(\d{1,})""\)\]")
                        .Match(DownloadServerVersion());

                if (!match.Success) return;
                var gitVersion =
                    new Version(
                        string.Format(
                            "{0}.{1}.{2}.{3}",
                            match.Groups[1],
                            match.Groups[2],
                            match.Groups[3],
                            match.Groups[4]));
                // makes sense to display your version and say what the current one is on github
                ColoredConsoleWrite(Color.Green, Properties.Strings.your_version + Assembly.GetExecutingAssembly().GetName().Version);
                ColoredConsoleWrite(Color.Green, Properties.Strings.github_version + gitVersion);
                ColoredConsoleWrite(Color.Green, Properties.Strings.github_link);
            }
            catch (Exception)
            {
                ColoredConsoleWrite(Color.Red, Properties.Strings.check_update_failed);
            }
        }

        private static string DownloadServerVersion()
        {
            using (var wC = new WebClient())
                return
                    wC.DownloadString(
                        "https://raw.githubusercontent.com/DetectiveSquirrel/Pokemon-Go-Rocket-API/master/PokemonGo/RocketAPI/Window/Properties/AssemblyInfo.cs");
        }

        public void ColoredConsoleWrite(Color color, string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<Color, string>(ColoredConsoleWrite), color, text);
                return;
            }

            logTextBox.Select(logTextBox.Text.Length, 1); // Reset cursor to last

            string textToAppend = "[" + DateTime.Now.ToString("HH:mm:ss tt") + "] " + text + "\r\n";
            logTextBox.SelectionColor = color;
            logTextBox.AppendText(textToAppend);

            object syncRoot = new object();
            lock (syncRoot) // Added locking to prevent text file trying to be accessed by two things at the same time
            {
                File.AppendAllText(AppDomain.CurrentDomain.BaseDirectory + @"\Logs.txt", "[" + DateTime.Now.ToString("HH:mm:ss tt") + "] " + text + "\n");
            }
        }

        public void ConsoleClear()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(ConsoleClear));
                return;
            }

            logTextBox.Clear();
        }

        public void SetStatusText(string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(SetStatusText), text);
                return;
            }

            statusLabel.Text = text;
        }

        private async Task EvolvePokemons(Client client)
        {
            var inventory = await client.GetInventory();
            var pokemons =
                inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                    .Where(p => p != null && p?.PokemonId > 0);

            await EvolveAllGivenPokemons(client, pokemons);
        }

        private async Task EvolveAllGivenPokemons(Client client, IEnumerable<PokemonData> pokemonToEvolve)
        {
            foreach (var pokemon in pokemonToEvolve)
            {
                /*
                enum Holoholo.Rpc.Types.EvolvePokemonOutProto.Result {
	                UNSET = 0;
	                SUCCESS = 1;
	                FAILED_POKEMON_MISSING = 2;
	                FAILED_INSUFFICIENT_RESOURCES = 3;
	                FAILED_POKEMON_CANNOT_EVOLVE = 4;
	                FAILED_POKEMON_IS_DEPLOYED = 5;
                }
                }*/

                var countOfEvolvedUnits = 0;
                var xpCount = 0;

                EvolvePokemonOut evolvePokemonOutProto;
                do
                {
                    evolvePokemonOutProto = await client.EvolvePokemon(pokemon.Id);
                    //todo: someone check whether this still works

                    if (evolvePokemonOutProto.Result == 1)
                    {
                        ColoredConsoleWrite(Color.Cyan,
                            string.Format(Properties.Strings.evolve_success, pokemon.PokemonId, evolvePokemonOutProto.ExpAwarded));

                        countOfEvolvedUnits++;
                        xpCount += evolvePokemonOutProto.ExpAwarded;
                    }
                    else
                    {
                        var result = evolvePokemonOutProto.Result;
                        /*
                        ColoredConsoleWrite(ConsoleColor.White, $"Failed to evolve {pokemon.PokemonId}. " +
                                                 $"EvolvePokemonOutProto.Result was {result}");

                        ColoredConsoleWrite(ConsoleColor.White, $"Due to above error, stopping evolving {pokemon.PokemonId}");
                        */
                    }
                } while (evolvePokemonOutProto.Result == 1);
                if (countOfEvolvedUnits > 0)
                    ColoredConsoleWrite(Color.Cyan,
                       string.Format(Properties.Strings.evolve_pieces, countOfEvolvedUnits, pokemon.PokemonId, xpCount));

                await Task.Delay(3000);
            }
        }

        private async void Execute()
        {
            client = new Client(ClientSettings);
            this.locationManager = new LocationManager(client, ClientSettings.TravelSpeed);
            try
            {
                switch (ClientSettings.AuthType)
                {
                    case AuthType.Ptc:
                        ColoredConsoleWrite(Color.Green, Properties.Strings.login_type_PTC);
                        break;
                    case AuthType.Google:
                        ColoredConsoleWrite(Color.Green, Properties.Strings.login_type_Google);
                        break;
                }

                await client.Login();
                await client.SetServer();
                var profile = await client.GetProfile();
                var settings = await client.GetSettings();
                var mapObjects = await client.GetMapObjects();
                var inventory = await client.GetInventory();
                var pokemons =
                    inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                        .Where(p => p != null && p?.PokemonId > 0);

                updateUserStatusBar(client);

                // Write the players ingame details
                ColoredConsoleWrite(Color.Yellow, "----------------------------");
                /*// dont actually want to display info but keeping here incase people want to \O_O/
                 * if (ClientSettings.AuthType == AuthType.Ptc)
                {
                    ColoredConsoleWrite(Color.Cyan, string.Format(Properties.Strings.account, ClientSettings.PtcUsername));
                    ColoredConsoleWrite(Color.Cyan, string.Format(Properties.Strings.password, ClientSettings.PtcPassword));
                }
                else
                {
                    ColoredConsoleWrite(Color.DarkGray, string.Format(Properties.Strings.name, profile.Profile.Username));
                    ColoredConsoleWrite(Color.DarkGray, string.Format(Properties.Strings.team, profile.Profile.Team));
                }*/
                string lat2 = System.Convert.ToString(ClientSettings.DefaultLatitude);
                string longit2 = System.Convert.ToString(ClientSettings.DefaultLongitude);
                ColoredConsoleWrite(Color.DarkGray, string.Format(Properties.Strings.name, profile.Profile.Username));
                ColoredConsoleWrite(Color.DarkGray, string.Format(Properties.Strings.team, profile.Profile.Team));
                if (profile.Profile.Currency.ToArray()[0].Amount > 0) // If player has any pokecoins it will show how many they have.
                    ColoredConsoleWrite(Color.DarkGray, string.Format(Properties.Strings.pokecoin, profile.Profile.Currency.ToArray()[0].Amount));
                ColoredConsoleWrite(Color.DarkGray, string.Format(Properties.Strings.stardust, profile.Profile.Currency.ToArray()[1].Amount));
                ColoredConsoleWrite(Color.DarkGray, string.Format(Properties.Strings.latitude, ClientSettings.DefaultLatitude));
                ColoredConsoleWrite(Color.DarkGray, string.Format(Properties.Strings.longitude, ClientSettings.DefaultLongitude));
                try
                {
                    ColoredConsoleWrite(Color.DarkGray, string.Format(Properties.Strings.country, CallAPI("country", lat2.Replace(',', '.'), longit2.Replace(',', '.'))));
                    ColoredConsoleWrite(Color.DarkGray, string.Format(Properties.Strings.area, CallAPI("place", lat2.Replace(',', '.'), longit2.Replace(',', '.'))));
                }
                catch (Exception)
                {
                    ColoredConsoleWrite(Color.DarkGray, Properties.Strings.error_country);
                }


                ColoredConsoleWrite(Color.Yellow, "----------------------------");

                //Choosing if else, because switch case handle only constant values
                if(ClientSettings.TransferType == Properties.Strings.TransferType_Strongest)
                    await TransferAllButStrongestUnwantedPokemon(client);
                else if(ClientSettings.TransferType == Properties.Strings.TransferType_All)
                    await TransferAllGivenPokemons(client, pokemons);
                else if(ClientSettings.TransferType == Properties.Strings.TransferType_Duplicate)
                    await TransferDuplicatePokemon(client);
                else if(ClientSettings.TransferType == Properties.Strings.TransferType_IV_Duplicate)
                    await TransferDuplicateIVPokemon(client);
                else if(ClientSettings.TransferType == Properties.Strings.TransferType_CP)
                    await TransferAllWeakPokemon(client, ClientSettings.TransferCPThreshold);
                else if(ClientSettings.TransferType == Properties.Strings.TransferType_IV)
                    await TransferAllGivenPokemons(client, pokemons, ClientSettings.TransferIVThreshold);
                else
                    ColoredConsoleWrite(Color.DarkGray, Properties.Strings.transfering_disabled);


                if (ClientSettings.EvolveAllGivenPokemons)
                    await EvolveAllGivenPokemons(client, pokemons);
                if (ClientSettings.Recycler)
                    client.RecycleItems(client);

                await Task.Delay(5000);
                PrintLevel(client);
                await ExecuteFarmingPokestopsAndPokemons(client);

                while (ForceUnbanning)
                    await Task.Delay(25);

                // await ForceUnban(client);

                if (!Stopping)
                {
                    ColoredConsoleWrite(Color.Red, Properties.Strings.no_location);
                    await Task.Delay(10000);
                    CheckVersion();
                    Execute();
                }
                else
                {
                    ConsoleClear();
                    pokestopsOverlay.Routes.Clear();
                    pokestopsOverlay.Markers.Clear();
                    ColoredConsoleWrite(Color.Red, Properties.Strings.bot_stop);
                    startStopBotToolStripMenuItem.Text = Properties.Strings.start;
                    Stopping = false;
                    bot_started = false;
                }
            }
            catch (Exception ex)
            {
                ColoredConsoleWrite(Color.Red, ex.ToString());
                if (!Stopping)
                {
                    client = null;
                    Execute();
                }
            }

        }

        private static string CallAPI(string elem, string lat, string lon)
        {

            using (XmlReader reader = XmlReader.Create(@"http://api.geonames.org/findNearby?lat=" + lat + "&lng=" + lon + "&username=pokemongobot"))
            {
                while (reader.Read())
                {
                    if (reader.IsStartElement())
                    {
                        switch (elem)
                        {
                            case "country":
                                if (reader.Name == "countryName")
                                {
                                    return reader.ReadString();
                                }
                                break;

                            case "place":
                                if (reader.Name == "name")
                                {
                                    return reader.ReadString();
                                }
                                break;
                            default:
                                return "N/A";
                                break;
                        }
                    }
                }
            }
            return Properties.Strings.error;
        }

        private async Task ExecuteCatchAllNearbyPokemons(Client client)
        {
            var mapObjects = await client.GetMapObjects();

            var pokemons = mapObjects.MapCells.SelectMany(i => i.CatchablePokemons);
            var inventory2 = await client.GetInventory();
            var pokemons2 = inventory2.InventoryDelta.InventoryItems
                .Select(i => i.InventoryItemData?.Pokemon)
                .Where(p => p != null && p?.PokemonId > 0)
                .ToArray();

            foreach (var pokemon in pokemons)
            {
                if (ForceUnbanning || Stopping)
                    break;

                FarmingPokemons = true;

                await locationManager.update(pokemon.Latitude, pokemon.Longitude);

                string pokemonName;
                if (ClientSettings.Language == "de")
                {
                    string name_english = Convert.ToString(pokemon.PokemonId);
                    var request = (HttpWebRequest)WebRequest.Create("http://boosting-service.de/pokemon/index.php?pokeName=" + name_english);
                    var response = (HttpWebResponse)request.GetResponse();
                    pokemonName = new StreamReader(response.GetResponseStream()).ReadToEnd();
                }
                else
                    pokemonName = Convert.ToString(pokemon.PokemonId);

                await client.UpdatePlayerLocation(pokemon.Latitude, pokemon.Longitude);
                UpdatePlayerLocation(pokemon.Latitude, pokemon.Longitude);
                UpdateMap();
                var encounterPokemonResponse = await client.EncounterPokemon(pokemon.EncounterId, pokemon.SpawnpointId);
                var pokemonCP = encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp;
                var pokemonIV = Math.Round(Perfect(encounterPokemonResponse?.WildPokemon?.PokemonData));
                CatchPokemonResponse caughtPokemonResponse;
                ColoredConsoleWrite(Color.Green, string.Format(Properties.Strings.encounter_pokemon, pokemonName, pokemonCP, pokemonIV));
                do
                {
                    if (ClientSettings.RazzBerryMode == Properties.Strings.RazzBerryMode_CP)
                        if (pokemonCP > ClientSettings.RazzBerrySetting)
                            await client.UseRazzBerry(client, pokemon.EncounterId, pokemon.SpawnpointId);
                    if (ClientSettings.RazzBerryMode == Properties.Strings.RazzBerryMode_probability)
                        if (encounterPokemonResponse.CaptureProbability.CaptureProbability_.First() < ClientSettings.RazzBerrySetting)
                            await client.UseRazzBerry(client, pokemon.EncounterId, pokemon.SpawnpointId);
                    caughtPokemonResponse = await client.CatchPokemon(pokemon.EncounterId, pokemon.SpawnpointId, pokemon.Latitude, pokemon.Longitude, MiscEnums.Item.ITEM_POKE_BALL, pokemonCP); ; //note: reverted from settings because this should not be part of settings but part of logic
                } while (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchMissed || caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchEscape);

                if (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchSuccess)
                {
                    Color c = Color.LimeGreen;
                    if (pokemonIV >= 80)
                    {
                        c = Color.Yellow;
                    }
                    ColoredConsoleWrite(Color.Green, string.Format(Properties.Strings.pokemon_caught, pokemonName, pokemonCP, pokemonIV));
                    foreach (int xp in caughtPokemonResponse.Scores.Xp)
                        TotalExperience += xp;
                    TotalPokemon += 1;
                }
                else
                    ColoredConsoleWrite(Color.Red, string.Format(Properties.Strings.pokemon_got_away, pokemonName, pokemonCP, pokemonIV));

                // Need else if, because switch case handle only constant values
                if(ClientSettings.TransferType == Properties.Strings.TransferType_Strongest)
                    await TransferAllButStrongestUnwantedPokemon(client);
                else if(ClientSettings.TransferType == Properties.Strings.TransferType_All)
                    await TransferAllGivenPokemons(client, pokemons2);
                else if(ClientSettings.TransferType == Properties.Strings.TransferType_Duplicate)
                    await TransferDuplicatePokemon(client);
                else if (ClientSettings.TransferType == Properties.Strings.TransferType_IV_Duplicate)
                    await TransferDuplicateIVPokemon(client);
                else if (ClientSettings.TransferType == Properties.Strings.TransferType_CP)
                    await TransferAllWeakPokemon(client, ClientSettings.TransferCPThreshold);
                else if (ClientSettings.TransferType == Properties.Strings.TransferType_IV)
                    await TransferAllGivenPokemons(client, pokemons2, ClientSettings.TransferIVThreshold);
                else
                    ColoredConsoleWrite(Color.DarkGray, Properties.Strings.transfering_disabled);

                FarmingPokemons = false;
                await Task.Delay(3000);
            }
            pokemons = null;
        }

        private void UpdatePlayerLocation(double latitude, double longitude)
        {
            synchronizationContext.Post(new SendOrPostCallback(o =>
            {
                playerMarker.Position = (PointLatLng)o;

                searchAreaOverlay.Polygons.Clear();

            }), new PointLatLng(latitude, longitude));

            ColoredConsoleWrite(Color.Gray, string.Format(Properties.Strings.moving_player, latitude, longitude));
        }

        private void UpdateMap()
        {
            synchronizationContext.Post(new SendOrPostCallback(o =>
            {
                pokestopsOverlay.Markers.Clear();
                List<PointLatLng> routePoint = new List<PointLatLng>();
                foreach (var pokeStop in pokeStops)
                {
                    GMarkerGoogleType type = GMarkerGoogleType.blue_small;
                    if (pokeStop.CooldownCompleteTimestampMs > DateTime.UtcNow.ToUnixTime())
                    {
                        type = GMarkerGoogleType.gray_small;
                    }
                    var pokeStopLoc = new PointLatLng(pokeStop.Latitude, pokeStop.Longitude);
                    var pokestopMarker = new GMarkerGoogle(pokeStopLoc, type);
                    //pokestopMarker.ToolTipMode = MarkerTooltipMode.OnMouseOver;
                    //pokestopMarker.ToolTip = new GMapBaloonToolTip(pokestopMarker);
                    pokestopsOverlay.Markers.Add(pokestopMarker);

                    routePoint.Add(pokeStopLoc);
                }
                pokestopsOverlay.Routes.Clear();
                pokestopsOverlay.Routes.Add(new GMapRoute(routePoint, "Walking Path"));


                pokemonsOverlay.Markers.Clear();
                if (wildPokemons != null)
                {
                    foreach (var pokemon in wildPokemons)
                    {
                        var pokemonMarker = new GMarkerGoogle(new PointLatLng(pokemon.Latitude, pokemon.Longitude),
                            GMarkerGoogleType.red_small);
                        pokemonsOverlay.Markers.Add(pokemonMarker);
                    }
                }

                S2GMapDrawer.DrawS2Cells(S2Helper.GetNearbyCellIds(ClientSettings.DefaultLongitude, ClientSettings.DefaultLatitude), searchAreaOverlay);
            }), null);
        }

        private async Task ExecuteFarmingPokestopsAndPokemons(Client client)
        {
            var mapObjects = await client.GetMapObjects();

            FortData[] rawPokeStops = mapObjects.MapCells.SelectMany(i => i.Forts).Where(i => i.Type == FortType.Checkpoint && i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime()).ToArray();
            pokeStops = PokeStopOptimizer.Optimize(rawPokeStops, ClientSettings.DefaultLatitude, ClientSettings.DefaultLongitude, pokestopsOverlay);
            wildPokemons = mapObjects.MapCells.SelectMany(i => i.WildPokemons);
            if (!ForceUnbanning && !Stopping)
                ColoredConsoleWrite(Color.Cyan, string.Format(Properties.Strings.visiting_pokestops, pokeStops.Count()));

            UpdateMap();

            foreach (var pokeStop in pokeStops)
            {
                if (ForceUnbanning || Stopping)
                    break;

                FarmingStops = true;
                await locationManager.update(pokeStop.Latitude, pokeStop.Longitude);
                UpdatePlayerLocation(pokeStop.Latitude, pokeStop.Longitude);
                UpdateMap();

                var fortInfo = await client.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                var fortSearch = await client.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                StringWriter PokeStopOutput = new StringWriter();
                PokeStopOutput.Write($"");
                if (fortInfo.Name != string.Empty)
                    PokeStopOutput.Write(Properties.Strings.pokestop + fortInfo.Name);
                if (fortSearch.ExperienceAwarded != 0)
                    PokeStopOutput.Write(string.Format(Properties.Strings.xp, fortSearch.ExperienceAwarded));
                if (fortSearch.GemsAwarded != 0)
                    PokeStopOutput.Write(string.Format(Properties.Strings.gems, fortSearch.GemsAwarded));
                if (fortSearch.PokemonDataEgg != null)
                    PokeStopOutput.Write(string.Format(Properties.Strings.eggs, fortSearch.PokemonDataEgg));
                if (GetFriendlyItemsString(fortSearch.ItemsAwarded) != string.Empty)
                    PokeStopOutput.Write(string.Format(Properties.Strings.items, GetFriendlyItemsString(fortSearch.ItemsAwarded)));
                ColoredConsoleWrite(Color.Cyan, PokeStopOutput.ToString());

                if (fortSearch.ExperienceAwarded != 0)
                    TotalExperience += (fortSearch.ExperienceAwarded);

                pokeStop.CooldownCompleteTimestampMs = DateTime.UtcNow.ToUnixTime() + 300000;

                if (ClientSettings.CatchPokemon)
                    await ExecuteCatchAllNearbyPokemons(client);
            }
            FarmingStops = false;
            if (!ForceUnbanning && !Stopping)
            {
                client.RecycleItems(client);
                await ExecuteFarmingPokestopsAndPokemons(client);
            }
        }

        private async Task ForceUnban(Client client)
        {
            if (!ForceUnbanning && !Stopping)
            {
                ColoredConsoleWrite(Color.LightGreen, Properties.Strings.waiting_farming);
                ForceUnbanning = true;

                while (FarmingStops || FarmingPokemons)
                {
                    await Task.Delay(25);
                }

                ColoredConsoleWrite(Color.LightGreen, Properties.Strings.start_unban);

                pokestopsOverlay.Routes.Clear();
                pokestopsOverlay.Markers.Clear();
                bool done = false;
                foreach (var pokeStop in pokeStops)
                {
                    if (pokeStop.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime())
                    {
                        await locationManager.update(pokeStop.Latitude, pokeStop.Longitude);
                        UpdatePlayerLocation(pokeStop.Latitude, pokeStop.Longitude);
                        UpdateMap();

                        var fortInfo = await client.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                        if (fortInfo.Name != string.Empty)
                        {
                            ColoredConsoleWrite(Color.LightGreen, string.Format(Properties.Strings.chose_pokestop, fortInfo.Name));
                            for (int i = 1; i <= 50; i++)
                            {
                                var fortSearch = await client.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                                if (fortSearch.ExperienceAwarded == 0)
                                {
                                    ColoredConsoleWrite(Color.LightGreen, Properties.Strings.attempt + i);
                                }
                                else
                                {
                                    ColoredConsoleWrite(Color.LightGreen, Properties.Strings.unbanned + i);
                                    done = true;
                                    break;
                                }
                            }
                            if (done)
                                break;
                        }
                    }
                }
                if (!done)
                    ColoredConsoleWrite(Color.LightGreen, Properties.Strings.unban_failed);
                ForceUnbanning = false;
            }
            else
            {
                ColoredConsoleWrite(Color.Red, Properties.Strings.action_in_play);
            }


        }

        private string GetFriendlyItemsString(IEnumerable<FortSearchResponse.Types.ItemAward> items)
        {
            var enumerable = items as IList<FortSearchResponse.Types.ItemAward> ?? items.ToList();

            if (!enumerable.Any())
                return string.Empty;

            return enumerable.GroupBy(i => i.ItemId)
                    .Select(kvp => new { ItemName = kvp.Key.ToString().Substring(4), Amount = kvp.Sum(x => x.ItemCount) })
                    .Select(y => $"{y.Amount}x {y.ItemName}")
                    .Aggregate((a, b) => $"{a}, {b}");
        }


        private async Task TransferAllButStrongestUnwantedPokemon(Client client)
        {
            var unwantedPokemonTypes = new List<PokemonId>();
            for (int i = 1; i <= 151; i++)
            {
                unwantedPokemonTypes.Add((PokemonId)i);
            }

            var inventory = await client.GetInventory();
            var pokemons = inventory.InventoryDelta.InventoryItems
                .Select(i => i.InventoryItemData?.Pokemon)
                .Where(p => p != null && p?.PokemonId > 0)
                .ToArray();

            foreach (var unwantedPokemonType in unwantedPokemonTypes)
            {
                var pokemonOfDesiredType = pokemons.Where(p => p.PokemonId == unwantedPokemonType)
                    .OrderByDescending(p => p.Cp)
                    .ToList();

                var unwantedPokemon =
                    pokemonOfDesiredType.Skip(1) // keep the strongest one for potential battle-evolving
                        .ToList();

                await TransferAllGivenPokemons(client, unwantedPokemon);
            }
        }

        public static float Perfect(PokemonData poke)
        {
            return ((float)(poke.IndividualAttack + poke.IndividualDefense + poke.IndividualStamina) / (3.0f * 15.0f)) * 100.0f;
        }

        private async Task TransferAllGivenPokemons(Client client, IEnumerable<PokemonData> unwantedPokemons, float keepPerfectPokemonLimit = 80.0f)
        {
            foreach (var pokemon in unwantedPokemons)
            {
                if (Perfect(pokemon) >= keepPerfectPokemonLimit) continue;
                ColoredConsoleWrite(Color.White, string.Format(Properties.Strings.IV_less, pokemon.PokemonId, pokemon.Cp, keepPerfectPokemonLimit));

                if (pokemon.Favorite == 0)
                {
                    var transferPokemonResponse = await client.TransferPokemon(pokemon.Id);

                    /*
                    ReleasePokemonOutProto.Status {
                        UNSET = 0;
                        SUCCESS = 1;
                        POKEMON_DEPLOYED = 2;
                        FAILED = 3;
                        ERROR_POKEMON_IS_EGG = 4;
                    }*/
                    string pokemonName;
                    if (ClientSettings.Language == "de")
                    {
                        // Dont really need to print this do we? youll know if its German or not
                        //ColoredConsoleWrite(Color.DarkCyan, "german");
                        string name_english = Convert.ToString(pokemon.PokemonId);
                        var request = (HttpWebRequest)WebRequest.Create("http://boosting-service.de/pokemon/index.php?pokeName=" + name_english);
                        var response = (HttpWebResponse)request.GetResponse();
                        pokemonName = new StreamReader(response.GetResponseStream()).ReadToEnd();
                    }
                    else
                        pokemonName = Convert.ToString(pokemon.PokemonId);
                    if (transferPokemonResponse.Status == 1)
                    {
                        ColoredConsoleWrite(Color.Magenta, string.Format(Properties.Strings.transfer, pokemonName, pokemon.Cp));
                    }
                    else
                    {
                        var status = transferPokemonResponse.Status;

                        ColoredConsoleWrite(Color.Red, string.Format(Properties.Strings.transfer_failed, pokemonName, pokemon.Cp, status));
                    }

                    await Task.Delay(3000);
                }
            }
        }

        private async Task TransferDuplicatePokemon(Client client)
        {

            //ColoredConsoleWrite(ConsoleColor.White, $"Check for duplicates");
            var inventory = await client.GetInventory();
            var allpokemons =
                inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                    .Where(p => p != null && p?.PokemonId > 0);

            var dupes = allpokemons.OrderBy(x => x.Cp).Select((x, i) => new { index = i, value = x })
                .GroupBy(x => x.value.PokemonId)
                .Where(x => x.Skip(1).Any());

            for (var i = 0; i < dupes.Count(); i++)
            {
                for (var j = 0; j < dupes.ElementAt(i).Count() - 1; j++)
                {
                    var dubpokemon = dupes.ElementAt(i).ElementAt(j).value;
                    if (dubpokemon.Favorite == 0)
                    {
                        var transfer = await client.TransferPokemon(dubpokemon.Id);
                        string pokemonName;
                        if (ClientSettings.Language == "de")
                        {
                            string name_english = Convert.ToString(dubpokemon.PokemonId);
                            var request = (HttpWebRequest)WebRequest.Create("http://boosting-service.de/pokemon/index.php?pokeName=" + name_english);
                            var response = (HttpWebResponse)request.GetResponse();
                            pokemonName = new StreamReader(response.GetResponseStream()).ReadToEnd();
                        }
                        else
                            pokemonName = Convert.ToString(dubpokemon.PokemonId);
                        ColoredConsoleWrite(Color.DarkGreen, string.Format(Properties.Strings.transfer_highest_CP, pokemonName, dubpokemon.Cp, dupes.ElementAt(i).Last().value.Cp));

                    }
                }
            }
        }

        private async Task TransferDuplicateIVPokemon(Client client)
        {

            //ColoredConsoleWrite(ConsoleColor.White, $"Check for duplicates");
            var inventory = await client.GetInventory();
            var allpokemons =
                inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                    .Where(p => p != null && p?.PokemonId > 0);

            var dupes = allpokemons.OrderBy(x => Perfect(x)).Select((x, i) => new { index = i, value = x })
                .GroupBy(x => x.value.PokemonId)
                .Where(x => x.Skip(1).Any());

            for (var i = 0; i < dupes.Count(); i++)
            {
                for (var j = 0; j < dupes.ElementAt(i).Count() - 1; j++)
                {
                    var dubpokemon = dupes.ElementAt(i).ElementAt(j).value;
                    if (dubpokemon.Favorite == 0)
                    {
                        var transfer = await client.TransferPokemon(dubpokemon.Id);
                        string pokemonName;
                        if (ClientSettings.Language == "de")
                        {
                            string name_english = Convert.ToString(dubpokemon.PokemonId);
                            var request = (HttpWebRequest)WebRequest.Create("http://boosting-service.de/pokemon/index.php?pokeName=" + name_english);
                            var response = (HttpWebResponse)request.GetResponse();
                            pokemonName = new StreamReader(response.GetResponseStream()).ReadToEnd();
                        }
                        else
                            pokemonName = Convert.ToString(dubpokemon.PokemonId);
                        ColoredConsoleWrite(Color.DarkGreen, string.Format(Properties.Strings.transfer_highest_IV, pokemonName, Math.Round(Perfect(dubpokemon)), Math.Round(Perfect(dupes.ElementAt(i).Last().value))));

                    }
                }
            }
        }

        private async Task TransferAllWeakPokemon(Client client, int cpThreshold)
        {
            //ColoredConsoleWrite(ConsoleColor.White, $"Firing up the meat grinder");

            PokemonId[] doNotTransfer = new[] //these will not be transferred even when below the CP threshold
            { // DO NOT EMPTY THIS ARRAY
                //PokemonId.Pidgey,
                //PokemonId.Rattata,
                //PokemonId.Weedle,
                //PokemonId.Zubat,
                //PokemonId.Caterpie,
                //PokemonId.Pidgeotto,
                //PokemonId.NidoranFemale,
                //PokemonId.Paras,
                //PokemonId.Venonat,
                //PokemonId.Psyduck,
                //PokemonId.Poliwag,
                //PokemonId.Slowpoke,
                //PokemonId.Drowzee,
                //PokemonId.Gastly,
                //PokemonId.Goldeen,
                //PokemonId.Staryu,
                PokemonId.Magikarp,
                PokemonId.Eevee//,
                //PokemonId.Dratini
            };

            var inventory = await client.GetInventory();
            var pokemons = inventory.InventoryDelta.InventoryItems
                                .Select(i => i.InventoryItemData?.Pokemon)
                                .Where(p => p != null && p?.PokemonId > 0)
                                .ToArray();

            //foreach (var unwantedPokemonType in unwantedPokemonTypes)
            {
                List<PokemonData> pokemonToDiscard;
                if (doNotTransfer.Count() != 0)
                    pokemonToDiscard = pokemons.Where(p => !doNotTransfer.Contains(p.PokemonId) && p.Cp < cpThreshold).OrderByDescending(p => p.Cp).ToList();
                else
                    pokemonToDiscard = pokemons.Where(p => p.Cp < cpThreshold).OrderByDescending(p => p.Cp).ToList();


                //var unwantedPokemon = pokemonOfDesiredType.Skip(1) // keep the strongest one for potential battle-evolving
                //                                          .ToList();
                ColoredConsoleWrite(Color.Gray, string.Format(Properties.Strings.grinding_start, pokemonToDiscard.Count, cpThreshold));
                await TransferAllGivenPokemons(client, pokemonToDiscard);

            }

            ColoredConsoleWrite(Color.Gray, Properties.Strings.grinding_finished);
        }



        public async Task PrintLevel(Client client)
        {
            var inventory = await client.GetInventory();
            var stats = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.PlayerStats).ToArray();
            foreach (var v in stats)
                if (v != null)
                {
                    int XpDiff = GetXpDiff(client, v.Level);
                    if (ClientSettings.LevelOutput == "time")
                        ColoredConsoleWrite(Color.Yellow, string.Format(Properties.Strings.current_lvl, v.Level, v.Experience - XpDiff, v.NextLevelXp - XpDiff));
                    else if (ClientSettings.LevelOutput == "levelup")
                        if (Currentlevel != v.Level)
                        {
                            Currentlevel = v.Level;
                            ColoredConsoleWrite(Color.Magenta, string.Format(Properties.Strings.lvlup, v.Level, v.NextLevelXp - v.Experience));
                        }
                }
            if (ClientSettings.LevelOutput == "levelup")
                await Task.Delay(1000);
            else
                await Task.Delay(ClientSettings.LevelTimeInterval * 1000);
            PrintLevel(client);
        }

        // Pulled from NecronomiconCoding
        public static string _getSessionRuntimeInTimeFormat()
        {
            return (DateTime.Now - InitSessionDateTime).ToString(@"dd\.hh\:mm\:ss");
        }

        public async Task updateUserStatusBar(Client client)
        {
            var inventory = await client.GetInventory();
            var stats = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.PlayerStats).ToArray();
            var profile = await client.GetProfile();
            Int16 hoursLeft = 0; Int16 minutesLeft = 0; Int32 secondsLeft = 0; double xpSec = 0;
            foreach (var v in stats)
                if (v != null)
                {
                    int XpDiff = GetXpDiff(client, v.Level);
                    //Calculating the exp needed to level up
                    Single expNextLvl = (v.NextLevelXp - v.Experience);
                    //Calculating the exp made per second
                    xpSec = (Math.Round(TotalExperience / GetRuntime()) / 60) / 60;
                    //Calculating the seconds left to level up
                    if (xpSec != 0)
                        secondsLeft = Convert.ToInt32((expNextLvl / xpSec));
                    //formatting data to make an output like DateFormat
                    while (secondsLeft > 60)
                    {
                        secondsLeft -= 60;
                        if (minutesLeft < 60)
                        {
                            minutesLeft++;
                        }
                        else
                        {
                            minutesLeft = 0;
                            hoursLeft++;
                        }
                    }
                    SetStatusText(string.Format(Properties.Strings.status, profile.Profile.Username, v.Level, _getSessionRuntimeInTimeFormat(), (v.Experience - v.PrevLevelXp - XpDiff), (v.NextLevelXp - v.PrevLevelXp - XpDiff), profile.Profile.Currency.ToArray()[1].Amount, Math.Round(TotalExperience / GetRuntime()), Math.Round(TotalPokemon / GetRuntime()), hoursLeft, minutesLeft, secondsLeft));
                }
            await Task.Delay(1000);
            updateUserStatusBar(client);
        }

        public static int GetXpDiff(Client client, int Level)
        {
            switch (Level)
            {
                case 1:
                    return 0;
                case 2:
                    return 1000;
                case 3:
                    return 2000;
                case 4:
                    return 3000;
                case 5:
                    return 4000;
                case 6:
                    return 5000;
                case 7:
                    return 6000;
                case 8:
                    return 7000;
                case 9:
                    return 8000;
                case 10:
                    return 9000;
                case 11:
                    return 10000;
                case 12:
                    return 10000;
                case 13:
                    return 10000;
                case 14:
                    return 10000;
                case 15:
                    return 15000;
                case 16:
                    return 20000;
                case 17:
                    return 20000;
                case 18:
                    return 20000;
                case 19:
                    return 25000;
                case 20:
                    return 25000;
                case 21:
                    return 50000;
                case 22:
                    return 75000;
                case 23:
                    return 100000;
                case 24:
                    return 125000;
                case 25:
                    return 150000;
                case 26:
                    return 190000;
                case 27:
                    return 200000;
                case 28:
                    return 250000;
                case 29:
                    return 300000;
                case 30:
                    return 350000;
                case 31:
                    return 500000;
                case 32:
                    return 500000;
                case 33:
                    return 750000;
                case 34:
                    return 1000000;
                case 35:
                    return 1250000;
                case 36:
                    return 1500000;
                case 37:
                    return 2000000;
                case 38:
                    return 2500000;
                case 39:
                    return 1000000;
                case 40:
                    return 1000000;
            }
            return 0;
        }

        public void confirmBotStopped()
        {
            //ConsoleClear(); // dont really want the console to be wipped on bot stop, unnecessary
            ColoredConsoleWrite(Color.Red, Properties.Strings.bot_stop);
            startStopBotToolStripMenuItem.Text = Properties.Strings.start;
            Stopping = false;
            bot_started = false;
        }

        private void logTextBox_TextChanged(object sender, EventArgs e)
        {
            logTextBox.SelectionStart = logTextBox.Text.Length;
            logTextBox.ScrollToCaret();
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SettingsForm settingsForm = new SettingsForm();
            settingsForm.Show();
        }

        private static bool bot_started = false;
        private void startStopBotToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!bot_started)
            {
                bot_started = true;
                startStopBotToolStripMenuItem.Text = Properties.Strings.stop_bot;
                Task.Run(() =>
                {
                    try
                    {
                        //ColoredConsoleWrite(ConsoleColor.White, "Coded by Ferox - edited by NecronomiconCoding");
                        CheckVersion();
                        Execute();
                    }
                    catch (PtcOfflineException)
                    {
                        ColoredConsoleWrite(Color.Red, Properties.Strings.ptc_error);
                    }
                    catch (Exception ex)
                    {
                        ColoredConsoleWrite(Color.Red, string.Format(Properties.Strings.unhandled_exception, ex));
                    }
                });
            }
            else
            {
                if (!ForceUnbanning)
                {
                    Stopping = true;
                    ColoredConsoleWrite(Color.Red, Properties.Strings.stopping_bot);
                }
                else
                {
                    ColoredConsoleWrite(Color.Red, Properties.Strings.action_in_play_unban);
                }
            }
        }

        private void Client_OnConsoleWrite(ConsoleColor color, string message)
        {
            Color c = Color.White;
            switch (color)
            {
                case ConsoleColor.Green:
                    c = Color.Green;
                    break;
                case ConsoleColor.DarkCyan:
                    c = Color.DarkCyan;
                    break;
            }
            ColoredConsoleWrite(c, message);
        }

        private void showAllToolStripMenuItem3_Click(object sender, EventArgs e)
        {
        }

        private void statsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // todo: add player stats later
        }

        private async void useLuckyEggToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (client != null)
            {
                try
                {
                    IEnumerable<Item> myItems = await client.GetItems(client);
                    IEnumerable<Item> LuckyEggs = myItems.Where(i => (ItemId)i.Item_ == ItemId.ItemLuckyEgg);
                    Item LuckyEgg = LuckyEggs.FirstOrDefault();
                    if (LuckyEgg != null)
                    {
                        var useItemXpBoostRequest = await client.UseItemXpBoost(ItemId.ItemLuckyEgg);
                        ColoredConsoleWrite(Color.Green, string.Format(Properties.Strings.using_lucky_egg, LuckyEgg.Count));
                        ColoredConsoleWrite(Color.Yellow, string.Format(Properties.Strings.lucky_egg_validity, DateTime.Now.AddMinutes(30).ToString()));

                        var stripItem = sender as ToolStripMenuItem;
                        stripItem.Enabled = false;
                        await Task.Delay(30000);
                        stripItem.Enabled = true;
                    }
                    else
                    {
                        ColoredConsoleWrite(Color.Red, Properties.Strings.no_lucky_egg);
                    }
                }
                catch (Exception ex)
                {
                    ColoredConsoleWrite(Color.Red, string.Format(Properties.Strings.lucky_egg_error, ex));
                }
            }
            else
            {
                ColoredConsoleWrite(Color.Red, Properties.Strings.lucky_egg_start_before);
            }
        }

        private async void forceUnbanToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (client != null && pokeStops != null)
            {
                if (ForceUnbanning)
                {
                    ColoredConsoleWrite(Color.Red, Properties.Strings.force_unban_in_action);
                }
                else
                {
                    await ForceUnban(client);
                }
            }
            else
            {
                ColoredConsoleWrite(Color.Red, Properties.Strings.force_unban_start_before);
            }
        }

        private void showAllToolStripMenuItem2_Click(object sender, EventArgs e)
        {

        }

        private void todoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SettingsForm settingsForm = new SettingsForm();
            settingsForm.Show();
        }

        private void pokemonToolStripMenuItem2_Click(object sender, EventArgs e)
        {
            var pForm = new PokeUi();
            pForm.Show();
        }



        #region POKEMON LIST
        private IEnumerable<PokemonFamily> families;


        private void InitializePokemonForm()
        {
            objectListView1.ButtonClick += PokemonListButton_Click;

            /* pkmnName.ImageGetter = delegate (object rowObject)
            {
                PokemonData pokemon = (PokemonData)rowObject;

                String key = pokemon.PokemonId.ToString();

                if (!objectListView1.SmallImageList.Images.ContainsKey(key))
                {
                    Image largeImage = GetPokemonImage((int)pokemon.PokemonId);
                    objectListView1.SmallImageList.Images.Add(key, largeImage);
                    objectListView1.LargeImageList.Images.Add(key, largeImage);
                }
                return key;
            };  */

            objectListView1.CellToolTipShowing += delegate (object sender, ToolTipShowingEventArgs args)
            {
                PokemonData pokemon = (PokemonData)args.Model;
                if (args.ColumnIndex == 8)
                {
                    int candyOwned = families
                            .Where(i => (int)i.FamilyId <= (int)pokemon.PokemonId)
                            .Select(f => f.Candy)
                            .First();
                    args.Text = candyOwned + " Candy";
                }
            };
        }

        private Image GetPokemonImage(int pokemonId)
        {
            var Sprites = AppDomain.CurrentDomain.BaseDirectory + "Sprites\\";
            string location = Sprites + pokemonId + ".png";
            if (!Directory.Exists(Sprites))
                Directory.CreateDirectory(Sprites);
            if (!File.Exists(location))
            {
                WebClient wc = new WebClient();
                wc.DownloadFile("http://pokeapi.co/media/sprites/pokemon/" + pokemonId + ".png", @location);
            }
            return Image.FromFile(location);
        }

        private async void ReloadPokemonList()
        {
            button1.Enabled = false;
            objectListView1.Enabled = false;

            client2 = new Client(ClientSettings);
            try
            {
                await client2.Login();
                await client2.SetServer();
                var inventory = await client2.GetInventory();
                var pokemons = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon).Where(p => p != null && p?.PokemonId > 0).OrderByDescending(key => key.Cp);
                families = inventory.InventoryDelta.InventoryItems
                   .Select(i => i.InventoryItemData?.PokemonFamily)
                   .Where(p => p != null && (int)p?.FamilyId > 0)
                   .OrderByDescending(p => (int)p.FamilyId);

                var currentScrollPosition = objectListView1.LowLevelScrollPosition;
                objectListView1.SetObjects(pokemons);
                objectListView1.LowLevelScroll(currentScrollPosition.X, currentScrollPosition.Y);
            }
            catch (Exception ex) { ColoredConsoleWrite(Color.Red, ex.ToString()); client2 = null; }

            button1.Enabled = true;
            objectListView1.Enabled = true;
        }

        private void PokemonListButton_Click(object sender, CellClickEventArgs e)
        {
            try
            {
                PokemonData pokemon = (PokemonData)e.Model;
                if (e.ColumnIndex == 6)
                {
                    TransferPokemon(pokemon);
                }
                else if (e.ColumnIndex == 7)
                {
                    PowerUpPokemon(pokemon);
                }
                else if (e.ColumnIndex == 8)
                {
                    EvolvePokemon(pokemon);
                }
            }
            catch (Exception ex) { ColoredConsoleWrite(Color.Red, ex.ToString()); client2 = null; ReloadPokemonList(); }
        }

        private async void TransferPokemon(PokemonData pokemon)
        {
            if (MessageBox.Show(string.Format(Properties.Strings.transfer_question, pokemon.PokemonId.ToString(), pokemon.Cp), Properties.Strings.transfer_title, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                var transferPokemonResponse = await client2.TransferPokemon(pokemon.Id);
                string message = "";
                string caption = "";

                if (transferPokemonResponse.Status == 1)
                {
                    message = string.Format(Properties.Strings.transfer_candy, pokemon.PokemonId, transferPokemonResponse.CandyAwarded);
                    caption = string.Format(Properties.Strings.transfer_candy_title, pokemon.PokemonId);
                    ReloadPokemonList();
                }
                else
                {
                    message = string.Format(Properties.Strings.transfer_error, pokemon.PokemonId);
                    caption = string.Format(Properties.Strings.transfer_error_title, pokemon.PokemonId);
                }

                MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private async void PowerUpPokemon(PokemonData pokemon)
        {
            var evolvePokemonResponse = await client2.PowerUp(pokemon.Id);
            string message = "";
            string caption = "";

            if (evolvePokemonResponse.Result == 1)
            {
                message = string.Format(Properties.Strings.upgrade_success, pokemon.PokemonId);
                caption = string.Format(Properties.Strings.upgrade_success_title, pokemon.PokemonId);
                ReloadPokemonList();
            }
            else
            {
                message = string.Format(Properties.Strings.upgrade_error, pokemon.PokemonId);
                caption = string.Format(Properties.Strings.upgrade_error_title, pokemon.PokemonId);
            }

            MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async void EvolvePokemon(PokemonData pokemon)
        {
            var evolvePokemonResponse = await client2.EvolvePokemon(pokemon.Id);
            string message = "";
            string caption = "";

            if (evolvePokemonResponse.Result == 1)
            {
                message = string.Format(Properties.Strings.evolved_success ,pokemon.PokemonId, evolvePokemonResponse.EvolvedPokemon.PokemonType, evolvePokemonResponse.ExpAwarded, evolvePokemonResponse.CandyAwarded);
                caption = string.Format(Properties.Strings.evolved_success_title, pokemon.PokemonId, evolvePokemonResponse.EvolvedPokemon.PokemonType);
                ReloadPokemonList();
            }
            else
            {
                message = string.Format(Properties.Strings.evolved_failed, pokemon.PokemonId);
                caption = string.Format(Properties.Strings.evolved_failed_title, pokemon.PokemonId);
            }

            MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ReloadPokemonList();
        }
        #endregion

        private void pokeToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void objectListView1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}
