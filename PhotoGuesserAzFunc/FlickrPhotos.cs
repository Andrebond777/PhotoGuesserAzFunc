using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using FlickrNet;
using PhotoGuesser.Data;
using HtmlAgilityPack; 
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.VisualBasic.FileIO;

namespace PhotoGuesserAzFunc;

public class FlickrPhotos
{
    private readonly FlickrNet.Flickr flickr;
    private readonly ILogger _logger;

    public FlickrPhotos(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<FlickrPhotos>();
        flickr = new FlickrNet.Flickr("cb1a986b128b33fede56de77f68293b7", "4fdae3558b204791");
    }
    
    [Function("GetImages")]
    public async Task<List<PhotoDesc>> GetImages([HttpTrigger(AuthorizationLevel.Anonymous, "POST",
            Route = "GetImages/{enableGeo}/{quantity}/{minYear}/{maxYear}")] HttpRequestData req, 
        bool enableGeo, int quantity, int minYear, int maxYear, [FromBody] string[] searchStrings)
    {
        var photoDescsList = new List<PhotoDesc>();
        Photo selectedPhoto = new Photo();
        for(int i = 0; i < quantity; i++)
        {
            selectedPhoto = new Photo();
            //this link is default for every uninitialized Photo object
            while (selectedPhoto.WebUrl.Equals("https://www.flickr.com/photos///"))
                selectedPhoto = await GetPh(enableGeo, minYear, maxYear, searchStrings);
            //replace all date taken 1/1/1980 because 99% of them are dateUploaded=dateTaken
            int year = selectedPhoto.DateTaken is { Year: 1980, Month: 1, Day: 1 } ? selectedPhoto.DateUploaded.Year : selectedPhoto.DateTaken.Year;
            
            photoDescsList.Add(new PhotoDesc(selectedPhoto.WebUrl, selectedPhoto.LargeUrl, year,
                selectedPhoto.Title, selectedPhoto.Latitude, selectedPhoto.Longitude));
        }
        return photoDescsList;
    }
    


    internal string GetContinent()
    {
        var continentsChance = new List<ContinentChance>();
        var overallChance = 0;
        using (TextFieldParser parser = new TextFieldParser(@"Data/continentSelectionChance.csv"))
        {
            parser.TextFieldType = FieldType.Delimited;
            parser.SetDelimiters(",");
            parser.ReadFields(); //skip first row
            while (!parser.EndOfData)
            {
                //Process row
                string[] fields = parser.ReadFields();
                string continentName = fields[0];
                int chanceMin = 0;
                if(continentsChance.Count > 0) 
                    chanceMin = overallChance+1;
                overallChance += Convert.ToInt32(fields[1]);
                continentsChance.Add(new ContinentChance(continentName, chanceMin, overallChance));
            }
        }
        Random rnd = new Random();
        int rndChanceContinent = rnd.Next(0, overallChance);
        var continent = continentsChance.Find(x => x.chanceMin <= rndChanceContinent && rndChanceContinent <= x.chanceMax).continent;
        return continent;
    }

    internal string GetCountry(string continent)
    {
        Random rnd = new Random();
        string country = "";
        if (continent.Equals("NorthAmerica"))
        {
            int rndChanceSelectUS = rnd.Next(0, 10);
            if (rndChanceSelectUS != 5)
                country = "United States";
        }
        if(country.Equals(""))
        {
            var countries = new List<string>();
            using (TextFieldParser parser = new TextFieldParser(
                       @"Data/CountriesByContinent/"
                       + "Countries" + continent + ".csv"))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(",");
                parser.ReadFields(); //skip first row
                while (!parser.EndOfData)
                {
                    //Process row
                    string[] fields = parser.ReadFields();
                    countries.Add(fields[0]);
                }
            }
            int rndChanceCountry = rnd.Next(0, countries.Count);
            country = countries[rndChanceCountry];
            if(country.Equals(""))
                country = "United States";
        }

        return country;
    }

    internal string GetCity(string country)
    {
        Random rnd = new Random();
        var cities = new List<string>();
        using (TextFieldParser parser = new TextFieldParser(
                   @"Data/CitiesByCountry/"
                   + country + ".csv"))
        {
            parser.TextFieldType = FieldType.Delimited;
            parser.SetDelimiters(",");
            parser.ReadFields(); //skip first row
            while (!parser.EndOfData)
            {
                //Process row
                string[] fields = parser.ReadFields();
                cities.Add(fields[0]);
            }
        }
            
        int rndCityIndex = rnd.Next(0, cities.Count);
        string result = cities[rndCityIndex];
        return result;
    }
    
    internal string GetSearchStr()
    {
        try
        {
            var continent = GetContinent();
            var country = GetCountry(continent);
            var city = GetCity(country);
            return city;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        //default
        return "New York";
    }
    
    internal async Task<Photo> GetPh(bool enableGeo, int minYear, int maxYear, string[] searchStrings)
    {
        Random rnd = new Random();
        DateTime datetoday = DateTime.Now;
        if (maxYear < 1830 || maxYear > datetoday.Year ||  minYear > maxYear)
            maxYear = datetoday.Year;
        if (minYear < 1830 || minYear > datetoday.Year || minYear > maxYear)
            minYear = 1830;
        int rndYear = rnd.Next(minYear + (maxYear - minYear)/2, maxYear);
        DateTime generatedDate = new DateTime(rndYear, 1, 1);

        string searchStr = GetSearchStr();
        Console.WriteLine(searchStr);
        var options = new PhotoSearchOptions { Text = searchStr, PerPage = 20, SortOrder = PhotoSearchSortOrder.None,
            MinTakenDate = new DateTime(minYear, 1, 1), MaxTakenDate = generatedDate, 
            Extras = PhotoSearchExtras.DateTaken | PhotoSearchExtras.DateUploaded | PhotoSearchExtras.Geo, HasGeo = enableGeo};
        
        var photos = new PhotoCollection();
        photos = await flickr.PhotosSearchAsync(options);
        if (photos.Pages < 1)
            return new Photo();
        
        var maxPage = 1;
        if (photos.Pages > 1)
            maxPage = photos.Pages - photos.Pages / 5;
        options.Page = rnd.Next(1, maxPage);
        
        photos = await flickr.PhotosSearchAsync(options);
        return photos[rnd.Next(0,photos.Count)];
    }
}