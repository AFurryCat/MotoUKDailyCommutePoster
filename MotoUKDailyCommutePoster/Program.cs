using Newtonsoft.Json.Linq;
using RedditSharp;
using RedditSharp.Things;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace MotoUKDailyCommutePoster
{
    class Program
    {
        private static readonly string metOfficeApiKey = "c03b4434-1c89-4a8e-a533-bf2abfae0c45";
        private static readonly string metOfficeQueryUrl = "http://datapoint.metoffice.gov.uk/public/data/txt/wxfcs/regionalforecast/json/515?key={0}";
        private static readonly string formattedWeatherString = "### Forecast\r\n\r\n#### {0}\r\n\r\n{1}\r\n\r\n#### {2}\r\n\r\n{3}\r\n\r\n#### {4}\r\n\r\n{5}\r\n\r\n### Sun times\r\n* {6}\r\n* {7}";

        private static readonly string sunQueryUrl = "http://api.sunrise-sunset.org/json?lat=51.507351&lng=-0.127758";
        private static readonly string sunQueryAdditional = "&date={0}";
       
        private static Reddit redditAccount;

        private static string user = "";
        private static string pass = "";
        private static string fileDirectory = "";

        private static int numberOfFoundFiles = 0;
        private static DateTime nextFileUploadTime = DateTime.MinValue;
        private static List<string> filesFound = new List<string>();
        private static int ticker = 0;
        private static bool isPosting = false;
        private static CultureInfo ukCulture = new CultureInfo("en-GB");

        static void Main(string[] args)
        {
            try
            {
                InitialiseApplication();

                Thread t = new Thread(CheckFilesAndProcessIfRequired);
                t.IsBackground = true;
                t.Start();

                while (true)
                {
                    PrintMainScreen();
                    Thread.Sleep(250);
                }
            }
            catch (Exception ex)
            {
                if (redditAccount != null)
                {
                    redditAccount.ComposePrivateMessage("MotoUK Daily Poster - Exception Thrown", ex.ToString(), "HiMyNamesMike");
                    redditAccount.ComposePrivateMessage("MotoUK Daily Poster - Error Occured", "You may need to post manually today and check nothing has stopped working server side!", redditAccount.User.ToString());
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("An error has occured:");
                    Console.WriteLine(ex.ToString());
                    Console.ReadLine();
                }
            }
        }

        private static void PrintTitleSequence()
        {
            Console.WriteLine("|-----------------------------------------------------------------------------|");
            Console.WriteLine("                MotoUK Daily Commute Poster - /u/HiMyNamesMike");
            Console.WriteLine("|-----------------------------------------------------------------------------|");
            Console.WriteLine();
        }

        private static void PrintMainScreen()
        {
            if (!isPosting)
            {
                Console.Clear();

                PrintTitleSequence();

                Console.WriteLine("Posting as: {0}", user);
                Console.WriteLine("Number of files found: {0}", numberOfFoundFiles);
                Console.WriteLine("Next upload: {0}", nextFileUploadTime.ToString("dd/MM/yyyy HH:mm"));
                Console.WriteLine();

                if (filesFound.Count > 0)
                {
                    Console.WriteLine("Files in folder:");

                    foreach (string file in filesFound)
                    {
                        Console.WriteLine("  - {0}", file.Substring(file.LastIndexOf("\\") + 1));
                    }
                }
                else
                {
                    Console.WriteLine("No files currently found in folder.");
                }

                Console.WriteLine();

                switch (ticker)
                {
                    case 0:
                        Console.WriteLine("                                        |");
                        ticker++;
                        break;
                    case 1:
                        Console.WriteLine("                                        /");
                        ticker++;
                        break;
                    case 2:
                        Console.WriteLine("                                        -");
                        ticker++;
                        break;
                    case 3:
                        Console.WriteLine("                                        \\");
                        ticker = 0;
                        break;
                }
            }
        }

        private static void InitialiseApplication()
        {
            PrintTitleSequence();

            Console.WriteLine("Loading settings...");

            user = ConfigurationManager.AppSettings["Username"].ToString();
            pass = ConfigurationManager.AppSettings["Password"].ToString();
            fileDirectory = ConfigurationManager.AppSettings["FileDirectory"].ToString();

            Console.WriteLine("Checking Reddit user...");

            redditAccount = new Reddit(user, pass);

            Console.WriteLine("Authenticated successfully!");
        }

        private static void CheckFilesAndProcessIfRequired()
        {
            int counter = 0;
            // fetch the en-GB culture
            

            filesFound = Directory.GetFiles(fileDirectory).ToList();
            numberOfFoundFiles = filesFound.Count;
            if (numberOfFoundFiles > 0)
                nextFileUploadTime = DateTime.Parse(filesFound.First().Substring(filesFound.First().LastIndexOf("\\") + 1, 15), ukCulture.DateTimeFormat);

            while (true)
            {
                filesFound = Directory.GetFiles(fileDirectory).ToList();
                numberOfFoundFiles = filesFound.Count;
                if (numberOfFoundFiles > 0)
                    nextFileUploadTime = DateTime.Parse(filesFound.First().Substring(filesFound.First().LastIndexOf("\\") + 1, 15), ukCulture.DateTimeFormat); 

                if (counter == 4)
                {
                    string currentFileToMatch = string.Format("{0}\\{1}.txt", fileDirectory, DateTime.Now.ToString("dd-MM-yyyy-HHmm"));
                    string postTitle = string.Format("Your Daily Commute  - {0}", DateTime.Now.Date.ToString("dd/MM/yyyy"));

                    if (filesFound.Contains(currentFileToMatch))
                    {
                        isPosting = true;

                        Console.Clear();

                        PrintTitleSequence();

                        Console.WriteLine("Preparing thread: {0}", postTitle);

                        string postContent = PreparePost(currentFileToMatch);

                        Console.WriteLine("Posting Thread: {0}", postTitle);

                        PostToMotoUK(redditAccount, postTitle, postContent);

                        Console.WriteLine("Thread Successfully Posted!");

                        Thread.Sleep(2000);

                        File.Delete(currentFileToMatch);

                        isPosting = false;
                    }

                    counter = 0;
                }

                counter++;
                Thread.Sleep(250);
            }
        }

        private static string PreparePost(string file)
        {
            Console.WriteLine("  Reading Post Content...");

            string postContent;
            using (StreamReader sr = new StreamReader(file))
            {
                postContent = sr.ReadToEnd();
            }

            Console.WriteLine("  Getting Weather Forecast...");

            JToken weatherData = JToken.Parse(FireWebRequest(string.Format(metOfficeQueryUrl, metOfficeApiKey)));

            JToken weather1 = weatherData["RegionalFcst"]["FcstPeriods"]["Period"][0]["Paragraph"][0];
            JToken weather2 = weatherData["RegionalFcst"]["FcstPeriods"]["Period"][0]["Paragraph"][1];
            JToken weather3 = weatherData["RegionalFcst"]["FcstPeriods"]["Period"][0]["Paragraph"][2];

            Console.WriteLine("  Calculating Sunrise and Sunset");

            JToken sunTodayData = JToken.Parse(FireWebRequest(sunQueryUrl));
            JToken sunTomorrowData = JToken.Parse(FireWebRequest(string.Concat(sunQueryUrl, string.Format(sunQueryAdditional, DateTime.Today.AddDays(1).ToString("yyyy-MM-dd")))));

            DateTime todaySunSet = DateTime.Parse(sunTodayData["results"]["sunset"].ToString(), ukCulture.DateTimeFormat);
            DateTime tomorrowSunRise = DateTime.Parse(sunTomorrowData["results"]["sunrise"].ToString(), ukCulture.DateTimeFormat);

            Console.WriteLine("  Formatting Weather Section");

            string weather = string.Format(formattedWeatherString,
                                            weather1["title"].ToString(),
                                            weather1["$"].ToString(),
                                            weather2["title"].ToString(),
                                            weather2["$"].ToString(),
                                            weather3["title"].ToString(),
                                            weather3["$"].ToString(),
                                            todaySunSet.ToString("HH:mm"),
                                            tomorrowSunRise.ToString("HH:mm"));

            postContent = postContent.Replace("[Weather]", weather);

            return postContent;
        }

        private static string FireWebRequest(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            try
            {
                WebResponse response = request.GetResponse();
                using (Stream responseStream = response.GetResponseStream())
                {
                    StreamReader reader = new StreamReader(responseStream, Encoding.UTF8);
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                WebResponse errorResponse = ex.Response;
                using (Stream responseStream = errorResponse.GetResponseStream())
                {
                    StreamReader reader = new StreamReader(responseStream, Encoding.GetEncoding("utf-8"));
                    String errorText = reader.ReadToEnd();
                    throw new Exception(errorText);
                }
            }
        }

        private static void PostToMotoUK(Reddit redditAccount, string postTitle, string postContent)
        {
            Subreddit motoUK = redditAccount.GetSubreddit("MotoUK");
            motoUK.SubmitTextPost(postTitle, postContent);
        }
    }
}