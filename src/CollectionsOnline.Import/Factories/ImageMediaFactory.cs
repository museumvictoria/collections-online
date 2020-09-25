using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CollectionsOnline.Core.Config;
using CollectionsOnline.Core.Indexes;
using CollectionsOnline.Core.Models;
using CollectionsOnline.Import.Extensions;
using CollectionsOnline.Import.Imports;
using CollectionsOnline.Import.Infrastructure;
using ImageMagick;
using IMu;
using Raven.Client;
using CollectionsOnline.Core.Extensions;
using CollectionsOnline.Core.Factories;
using Serilog;

namespace CollectionsOnline.Import.Factories
{
    public class ImageMediaFactory : IImageMediaFactory
    {
        private readonly IDocumentStore _documentStore;
        private readonly IImuSessionProvider _imuSessionProvider;
        private readonly IList<ImageMediaJob> _imageMediaJobs;

        public ImageMediaFactory(
            IDocumentStore documentStore,
            IImuSessionProvider imuSessionProvider)
        {
            _documentStore = documentStore;
            _imuSessionProvider = imuSessionProvider;

            // Build a list the various image conversions used in the application
            _imageMediaJobs = new List<ImageMediaJob>
            {
                new ImageMediaJob
                {                    
                    FileDerivativeType = FileDerivativeType.Large,
                    Transform = (imageMedia, magickImage, map) =>
                    {
                        AutoRotateImage(magickImage);
                        AddImageProfile(imageMedia, magickImage, true);
                        
                        magickImage.Format = MagickFormat.Jpeg;
                        magickImage.Resize(new MagickGeometry(3000) { Greater = true });
                        magickImage.Quality = 90;

                        return magickImage;
                    }
                },
                new ImageMediaJob
                {
                    FileDerivativeType = FileDerivativeType.Thumbnail,
                    Transform = (imageMedia, magickImage, map) =>
                    {
                        AutoRotateImage(magickImage);
                        AddImageProfile(imageMedia, magickImage);
                        
                        magickImage.Format = MagickFormat.Jpeg;
                        if (NeedsPadding(map))
                        {
                            magickImage.Resize(new MagickGeometry(250));
                            magickImage.Extent(new MagickGeometry(250), Gravity.Center, MagickColors.White);
                        }
                        else
                        {
                            magickImage.Resize(new MagickGeometry(250) { FillArea = true });
                            magickImage.Crop(250, 250, Gravity.Center);
                        }
                        magickImage.Quality = 74;

                        return magickImage;
                    }
                },
                new ImageMediaJob
                {
                    FileDerivativeType = FileDerivativeType.Small,
                    Transform = (imageMedia, magickImage, map) =>
                    {
                        AutoRotateImage(magickImage);
                        AddImageProfile(imageMedia, magickImage, true);

                        magickImage.Format = MagickFormat.Jpeg;
                        magickImage.Resize(new MagickGeometry(0, 500));
                        magickImage.Quality = 80;

                        return magickImage;
                    }
                },
                new ImageMediaJob
                {
                    FileDerivativeType = FileDerivativeType.Medium,
                    Transform = (imageMedia, magickImage, map) =>
                    {
                        AutoRotateImage(magickImage);
                        AddImageProfile(imageMedia, magickImage, true);

                        magickImage.Format = MagickFormat.Jpeg;
                        magickImage.Resize(new MagickGeometry(1500) { Greater = true });
                        magickImage.Quality = 80;

                        return magickImage;
                    }
                }
            };
        }

        public bool Make(ref ImageMedia imageMedia)
        {
            var stopwatch = Stopwatch.StartNew();

            if (FileExists(ref imageMedia))
            {
                stopwatch.Stop();
                Log.Logger.Debug("Found existing image {Irn} in {ElapsedMilliseconds} ms", imageMedia.Irn, stopwatch.ElapsedMilliseconds);

                return true;
            }

            // Fetch from Imu as we were not able to find local files
            try
            {
                using (var imuSession = _imuSessionProvider.CreateInstance("emultimedia"))
                {
                    imuSession.FindKey(imageMedia.Irn);
                    
                    var result = imuSession.Fetch("start", 0, -1, Columns).Rows[0];

                    var resource = result.GetMap("resource");
                    if (resource == null)
                        throw new IMuException("MultimediaResourceNotFound");
                    var fileStream = resource["file"] as FileStream;                    

                    using (var originalImage = new MagickImage(fileStream))
                    {
                        foreach (var imageMediaJob in _imageMediaJobs)
                        {
                            var destPath = PathFactory.MakeDestPath(imageMedia.Irn, ".jpg", imageMediaJob.FileDerivativeType);
                            var uriPath = PathFactory.BuildUriPath(imageMedia.Irn, ".jpg", imageMediaJob.FileDerivativeType);

                            using (var image = imageMediaJob.Transform(imageMedia, originalImage.Clone(), result))
                            {
                                // Write image to disk
                                image.Write(destPath);

                                // Set property via reflection (ImageMediaFile properties are used instead of a collection due to Raven Indexing)
                                typeof(ImageMedia)
                                    .GetProperties()
                                    .First(x => x.PropertyType == typeof(ImageMediaFile) && x.Name == imageMediaJob.FileDerivativeType.ToString())
                                    .SetValue(imageMedia, new ImageMediaFile
                                    {
                                        Uri = uriPath,
                                        Size = new FileInfo(destPath).Length,
                                        Width = image.Width,
                                        Height = image.Height
                                    });
                            }
                        }
                    }
                    
                    stopwatch.Stop();
                    Log.Logger.Debug("Completed image {Irn} ({Bytes}) creation in {stopwatch} ms", imageMedia.Irn, fileStream.Length.BytesToString(), stopwatch.ElapsedMilliseconds);

                    return true;
                }
            }
            catch (Exception ex)
            {
                if (ex is IMuException && ((IMuException)ex).ID == "MultimediaResolutionNotFound")
                {
                    Log.Logger.Warning(ex, "Multimedia resolution not found, unable to save image {Irn}", imageMedia.Irn);
                }
                else if (ex is IMuException && ((IMuException)ex).ID == "MultimediaResourceNotFound")
                {
                    Log.Logger.Warning(ex, "Multimedia resource not found, unable to save image {Irn}", imageMedia.Irn);
                }
                else if (ex is MagickCoderErrorException || ex is MagickCorruptImageErrorException)
                {
                    Log.Logger.Warning(ex, "Multimedia resource appears to be corrupt {Irn}", imageMedia.Irn);
                }
                else
                {
                    // Error is unexpected therefore we want the entire import to fail
                    Log.Logger.Fatal(ex, "Unexpected error occured creating image {Irn}", imageMedia.Irn);
                    throw;
                }
            }

            return false;
        }

        private bool FileExists(ref ImageMedia imageMedia)
        {
            // First check to see if we are not simply overwriting all existing media
            if (bool.Parse(ConfigurationManager.AppSettings["OverwriteExistingMedia"])) 
                return false;

            var mediaIrn = imageMedia.Irn;

            // Check to see whether the file has changed in emu first
            using (var documentSession = _documentStore.OpenSession())
            {
                // Find the latest document who's media contains the media we are checking
                var result = documentSession
                    .Query<MediaByIrnWithChecksumResult, MediaByIrnWithChecksum>()
                    .OrderByDescending(x => x.DateModified)
                    .FirstOrDefault(x => x.Irn == mediaIrn);

                // If all imu imports have run before (not simply loading images from disk) and there are no results that use this media and we need to save file
                var allImportsComplete = documentSession
                    .Load<Application>(Constants.ApplicationId)
                    .ImportStatuses.Where(x => x.ImportType.Contains(typeof(ImuImport<>).Name, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.PreviousDateRun)
                    .All(x => x.HasValue);

                if (allImportsComplete && result == null)
                    return false;

                // If existing media checksum does not match the one from emu we need to save file
                if (result != null && result.Md5Checksum != imageMedia.Md5Checksum)
                {
                    Log.Logger.Warning("Existing image {Irn} found but checksum {ExistingChecksum} did not match new image {NewChecksum}", imageMedia.Irn, result.Md5Checksum, imageMedia.Md5Checksum);
                    return false;
                }
            }

            // then check whether media exists on disk for all media jobs
            if (_imageMediaJobs.All(x => File.Exists(PathFactory.GetDestPath(mediaIrn, ".jpg", x.FileDerivativeType))))
            {
                foreach (var imageMediaJob in _imageMediaJobs)
                {
                    var destPath = PathFactory.GetDestPath(mediaIrn, ".jpg", imageMediaJob.FileDerivativeType);

                    using (var image = new MagickImage(destPath))
                    {
                        // Set property via reflection (ImageMediaFile properties are used instead of a collection due to Raven Indexing)
                        typeof(ImageMedia).GetProperties().First(x => x.PropertyType == typeof(ImageMediaFile) && x.Name == imageMediaJob.FileDerivativeType.ToString())
                            .SetValue(imageMedia, new ImageMediaFile
                            {
                                Uri = PathFactory.BuildUriPath(imageMedia.Irn, ".jpg", imageMediaJob.FileDerivativeType),
                                Size = new FileInfo(destPath).Length,
                                Width = image.Width,
                                Height = image.Height
                            });
                    }
                }
                    
                return true;
            }

            return false;
        }

        private string[] Columns
        {
            get
            {
                return new[]
                {
                    "resource",
                    "catmedia=<ecatalogue:MulMultiMediaRef_tab>.(irn,MdaDataSets_tab,ColScientificGroup)",
                    "narmedia=<enarratives:MulMultiMediaRef_tab>.(irn,DetPurpose_tab)",
                    "parmedia=<eparties:MulMultiMediaRef_tab>.(narmedia=<enarratives:NarAuthorsRef_tab>.(irn,DetPurpose_tab))"
                };
            }
        }

        private bool NeedsPadding(Map map)
        {
            // Pad media if we find any catalogue records that are marked specimen and are zoology records or we find media is used on a species record
            if (map.GetMaps("catmedia").Any(x => x != null &&
                x.GetEncodedStrings("MdaDataSets_tab").Contains(Constants.ImuSpecimenQueryString) &&
                x.GetEncodedString("ColScientificGroup").Contains("zoology", StringComparison.OrdinalIgnoreCase)) ||
                map.GetMaps("narmedia").Any(x => x != null &&
                x.GetEncodedStrings("DetPurpose_tab").Contains(Constants.ImuSpeciesQueryString)))
                return true;

            return false;
        }

        private void AddImageProfile(ImageMedia imageMedia, MagickImage magickImage, bool addIptcProfile = false)
        {
            // Save colour profile
            var colourProfile = magickImage.GetColorProfile();

            // Strip all profiles
            magickImage.Strip();

            // Add original colour profile back
            if (colourProfile != null)
                magickImage.AddProfile(colourProfile);

            if (addIptcProfile)
            {
                var iptcProfile = new IptcProfile();

                iptcProfile.SetValue(IptcTag.CopyrightNotice, imageMedia.Licence.Name);
                
                if (!string.IsNullOrWhiteSpace(imageMedia.Credit))
                    iptcProfile.SetValue(IptcTag.Credit, imageMedia.Credit);

                if(imageMedia.Sources.Any())
                    iptcProfile.SetValue(IptcTag.Source, imageMedia.Sources.Concatenate(", "));

                magickImage.AddProfile(iptcProfile);
            }
        }
        
        private void AutoRotateImage(MagickImage magickImage)
        {
            switch (magickImage.Orientation)
            {
                case OrientationType.TopLeft:
                case OrientationType.Undefined:
                    break;
                case OrientationType.TopRight:
                    magickImage.Flop();
                    break;
                case OrientationType.BottomRight:
                    magickImage.Rotate(180);
                    break;
                case OrientationType.BottomLeft:
                    magickImage.Flop();
                    magickImage.Rotate(180);
                    break;
                case OrientationType.LeftTop:
                    magickImage.Flop();
                    magickImage.Rotate(-90);
                    break;
                case OrientationType.RightTop:
                    magickImage.Rotate(90);
                    break;
                case OrientationType.RightBottom:
                    magickImage.Flop();
                    magickImage.Rotate(90);
                    break;
                case OrientationType.LeftBotom:
                    magickImage.Rotate(-90);
                    break;
            }

            magickImage.Orientation = OrientationType.TopLeft;
        }

        private class ImageMediaJob
        {
            public FileDerivativeType FileDerivativeType { get; set; }

            public Func<ImageMedia, MagickImage, Map, MagickImage> Transform { get; set; }
        }
    }
}