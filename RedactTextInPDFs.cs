using iTextSharp.text.log;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using iTextSharp.xtra.iTextSharp.text.pdf.pdfcleanup;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RedactTextInPDFs
{
    class RedactTextInPDFs
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        //private string TEXTTOBEFOUND = "Producerad av PMF - Presenterad via PMF Web";

        static int Main(string[] args)
        {
            string inDir = "";
            string outDir = "";
            string textToSearchAfter = "";
            string regex = "";

            try
            {
                RedactTextInPDFs redactSTLists = new RedactTextInPDFs();

                Console.WriteLine("Starting...");
                logger.Info("Starting...");

                if (args.Length < 3 || args.Length > 4)
                {
                    Console.WriteLine("Arguments are: <in_folder> <out_folder> <TextToSearchAfter> (optional)<RegexForWhichPDFsAreToBeLoaded>");
                    logger.Info("Arguments are: <in_folder> <out_folder> <TextToSearchAfter> (optional)<RegexForWhichPDFsAreToBeLoaded>");
                    Environment.Exit(1);
                }

                inDir = args[0].Trim();
                outDir = args[1].Trim();
                textToSearchAfter = args[2].Trim();

                if (args.Length == 4)
                {
                    regex = args[3].Trim();
                }

                if (!Directory.Exists(inDir) || !Directory.Exists(outDir))
                {
                    Console.WriteLine("One or both input directories do not exist.");
                    logger.Info("One or both input directories do not exist.");
                    Environment.Exit(1);
                }

                if(textToSearchAfter.Equals(""))
                {
                    Console.WriteLine("Text to search after is empty.");
                    logger.Info("Text to search after is empty.");
                    Environment.Exit(1);
                }
                
                if(!(outDir.EndsWith(@"\") || outDir.EndsWith(@"/")))
                {
                    outDir = outDir + @"\";
                }

                logger.Info("Input directory: " + inDir);
                logger.Info("Output directory: " + outDir);
                logger.Info("Text to search after: " + textToSearchAfter);
                logger.Info("Regex: " + regex);

                redactSTLists.SearchAndRedact(inDir, outDir, textToSearchAfter, regex);

                Console.WriteLine("Redact done.");
                logger.Info("Redact done.");
            }
            catch (Exception e)
            {
                logger.Error(e, "Application ended with fatal error: " + e.Message, args);
                Console.WriteLine("Application ended with fatal error (check log for more information): " + e.Message);
            }

            return 0;
        }

        public void SearchAndRedact(string inDir, string outDir, string textToSearchAfter, string regexStr)
        {
            List<RectAndText> RATs = null;
            string newFilePath = "";
            System.IO.DirectoryInfo directoryInfo = null;
            int counterSearchedFiles = 0;
            int counterRedactedFiles = 0;

            Regex regex = new Regex(regexStr);

            // Turns off AGPL licence note when looping many times (thousands).
            CounterFactory.getInstance().SetCounter(new NoOpCounter());

            List<string> files = Directory.GetFiles(inDir, "*.pdf", System.IO.SearchOption.AllDirectories)
                                 .Where(path => regex.IsMatch(System.IO.Path.GetFileName(path)))
                                 .ToList();


            foreach (string filePath in files)
            {
                logger.Debug("Checking if following file needs redact: " + filePath);
                counterSearchedFiles++;

                if(counterSearchedFiles % 100 == 0)
                {
                    logger.Info("Number of searched files: " + counterSearchedFiles);
                    Console.WriteLine("Number of searched files: " + counterSearchedFiles);
                }

                RATs = GetTextAndCoords(filePath, textToSearchAfter);

                if(RATs.Count != 0)
                {
                    logger.Info("Executing redact on: " + filePath);

                    directoryInfo = System.IO.Directory.GetParent(filePath);

                    newFilePath = outDir + directoryInfo.Name + @"\" + System.IO.Path.GetFileName(filePath);

                    new FileInfo(newFilePath).Directory.Create();

                    Redact(filePath, newFilePath, RATs);

                    logger.Info("Done... saved to: " + newFilePath);

                    counterRedactedFiles++;
                    if (counterRedactedFiles % 100 == 0)
                    {
                        logger.Info("Number of redacted files: " + counterRedactedFiles);
                        Console.WriteLine("Number of redacted files: " + counterRedactedFiles);
                    }
                }
            }

            logger.Info("Total number of searched files: " + counterSearchedFiles);
            Console.WriteLine("Total number of searched files: " + counterSearchedFiles);
            logger.Info("Total number of redacted files: " + counterRedactedFiles);
            Console.WriteLine("Total number of redacted files: " + counterRedactedFiles);

        }

        private List<RectAndText> GetTextAndCoords(string file, string textToBeFound)
        {

            List<RectAndText> returnRATs = new List<RectAndText>();

            StringBuilder text = new StringBuilder();
            ITextExtractionStrategy strategy = new MyLocationTextExtractionStrategy();

            using (PdfReader reader = new PdfReader(file))
            {
                for (int page = 1; page <= reader.NumberOfPages; page++)
                {
                    ((MyLocationTextExtractionStrategy)strategy).Page = page; 

                    string currentText = PdfTextExtractor.GetTextFromPage(reader, page, strategy);

                    text.Append(currentText);
                }
            }

            foreach (RectAndText RAT in ((MyLocationTextExtractionStrategy)strategy).myPoints)
            {
                if (RAT.Text.Equals(textToBeFound))
                {
                    logger.Debug("Text found: " + RAT.Text + " Page: " + RAT.Page + " Coords: " + RAT.Rect.GetTop(0) + ", " + RAT.Rect.GetBottom(0) + ", " + RAT.Rect.GetLeft(0) + ", " + RAT.Rect.GetRight(0));

                    returnRATs.Add(RAT);
                }
            }

            return returnRATs;
        }

        private void Redact(string inFile, string outFile, List<RectAndText> RATs)
        {
            using (Stream stream = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                PdfReader pdfReader = new PdfReader(stream);

                using (PdfStamper stamper = new PdfStamper(pdfReader, new FileStream(outFile, FileMode.OpenOrCreate)))
                {
                    List<PdfCleanUpLocation> cleanUpLocations = new List<PdfCleanUpLocation>();

                    foreach(RectAndText RAT in RATs)
                    {
                        cleanUpLocations.Add(new PdfCleanUpLocation(RAT.Page, RAT.Rect, iTextSharp.text.BaseColor.WHITE));
                    }

                    PdfCleanUpProcessor cleaner = new PdfCleanUpProcessor(cleanUpLocations, stamper);

                    cleaner.CleanUp();
                }

            }
        }

        private class MyLocationTextExtractionStrategy : LocationTextExtractionStrategy
        {
            public List<RectAndText> myPoints = new List<RectAndText>();
            public int Page = 1;

            public override void RenderText(TextRenderInfo renderInfo)
            {
                base.RenderText(renderInfo);

                Vector bottomLeft = renderInfo.GetDescentLine().GetStartPoint();
                Vector topRight = renderInfo.GetAscentLine().GetEndPoint();

                var rect = new iTextSharp.text.Rectangle(
                                                        bottomLeft[Vector.I1],
                                                        bottomLeft[Vector.I2],
                                                        topRight[Vector.I1],
                                                        topRight[Vector.I2]
                                                        );

                this.myPoints.Add(new RectAndText(rect, renderInfo.GetText(), Page));
            }
        }


        private class RectAndText
        {
            public iTextSharp.text.Rectangle Rect;
            public String Text;
            public int Page;
            public RectAndText(iTextSharp.text.Rectangle rect, String text, int page)
            {
                this.Rect = rect;
                this.Text = text;
                this.Page = page;
            }
        }
    }
}
