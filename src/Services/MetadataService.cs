using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;

namespace QuickAdd.Services
{
    /// <summary>
    /// Fills in missing metadata for games using the metadata plugins installed in Playnite (IGDB first). Every plugin
    /// is asked until all fields are filled; fields the game already has are never overwritten.
    /// Note: the SDK gives extensions no way to start Playnite's own metadata download, so this mirrors it.
    /// </summary>
    public static class MetadataService
    {
        sealed class Img { public byte[] Data; public string Ext; }

        sealed class Fetched
        {
            public string Description;
            public Img Cover, Background, Icon;
            public List<MetadataProperty> Genres, Developers, Publishers, Tags, Features, Series;
            public List<Link> Links;
            public ReleaseDate? Date;
            public int? Community, Critic;

            public bool HasAnything
            {
                get
                {
                    return !string.IsNullOrWhiteSpace(Description) || Cover != null || Background != null || Icon != null ||
                           Genres != null || Developers != null || Publishers != null || Tags != null || Features != null ||
                           Series != null || Links != null || Date != null || Community != null || Critic != null;
                }
            }
        }

        public static List<string> SourceNames(IPlayniteAPI api)
        {
            try { return api.Addons.Plugins.OfType<MetadataPlugin>().Select(p => p.Name).Distinct().OrderBy(n => n).ToList(); }
            catch { return new List<string>(); }
        }

        /// <summary>Runs on a background thread; game changes are applied on the UI thread. Returns games updated.</summary>
        public static int Run(IPlayniteAPI api, IList<Game> games, string source, string tmpDir,
            Action<int, int, string> progress, Func<bool> cancelled)
        {
            List<MetadataPlugin> plugins;
            try
            {
                plugins = api.Addons.Plugins.OfType<MetadataPlugin>()
                    .Where(p => string.IsNullOrEmpty(source) || p.Name == source)
                    .OrderBy(p => p.Name == "IGDB" ? 0 : 1).ToList();
            }
            catch { return 0; }
            if (plugins.Count == 0) return 0;

            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            int updated = 0;
            for (int i = 0; i < games.Count && !cancelled(); i++)
            {
                var game = games[i];
                progress(i, games.Count, game.Name);

                var f = new Fetched();
                foreach (var plugin in plugins)
                {
                    if (cancelled()) break;
                    try
                    {
                        using (var provider = plugin.GetMetadataProvider(new MetadataRequestOptions(game, true)))
                            Gather(game, provider, f);
                    }
                    catch { }
                    if (!StillMissing(game, f)) break; // everything is filled, no need to ask the next plugin
                }
                if (!f.HasAnything) continue;

                Action apply = () =>
                {
                    try { if (Apply(api, game, f, tmpDir)) { api.Database.Games.Update(game); updated++; } }
                    catch { }
                };
                var d = api.MainView.UIDispatcher;
                if (d != null) d.Invoke(apply); else apply();
            }
            return updated;
        }

        // ----- gather (network, off the UI thread) -----

        static T Safe<T>(Func<T> f) { try { return f(); } catch { return default(T); } }
        static bool Empty(List<Guid> l) { return l == null || l.Count == 0; }

        static void Gather(Game game, OnDemandMetadataProvider p, Fetched f)
        {
            var fields = p.AvailableFields ?? new List<MetadataField>();
            var args = new GetMetadataFieldArgs();

            if (f.Description == null && string.IsNullOrEmpty(game.Description) && fields.Contains(MetadataField.Description))
                f.Description = Safe(() => p.GetDescription(args));
            if (f.Cover == null && string.IsNullOrEmpty(game.CoverImage) && fields.Contains(MetadataField.CoverImage))
                f.Cover = Read(Safe(() => p.GetCoverImage(args)));
            if (f.Background == null && string.IsNullOrEmpty(game.BackgroundImage) && fields.Contains(MetadataField.BackgroundImage))
                f.Background = Read(Safe(() => p.GetBackgroundImage(args)));
            if (f.Icon == null && string.IsNullOrEmpty(game.Icon) && fields.Contains(MetadataField.Icon))
                f.Icon = Read(Safe(() => p.GetIcon(args)));

            if (f.Genres == null && Empty(game.GenreIds) && fields.Contains(MetadataField.Genres))
                f.Genres = AsList(Safe(() => p.GetGenres(args)));
            if (f.Developers == null && Empty(game.DeveloperIds) && fields.Contains(MetadataField.Developers))
                f.Developers = AsList(Safe(() => p.GetDevelopers(args)));
            if (f.Publishers == null && Empty(game.PublisherIds) && fields.Contains(MetadataField.Publishers))
                f.Publishers = AsList(Safe(() => p.GetPublishers(args)));
            if (f.Tags == null && Empty(game.TagIds) && fields.Contains(MetadataField.Tags))
                f.Tags = AsList(Safe(() => p.GetTags(args)));
            if (f.Features == null && Empty(game.FeatureIds) && fields.Contains(MetadataField.Features))
                f.Features = AsList(Safe(() => p.GetFeatures(args)));
            if (f.Series == null && Empty(game.SeriesIds) && fields.Contains(MetadataField.Series))
                f.Series = AsList(Safe(() => p.GetSeries(args)));

            if (f.Date == null && game.ReleaseDate == null && fields.Contains(MetadataField.ReleaseDate))
                f.Date = Safe(() => p.GetReleaseDate(args));
            if (f.Links == null && (game.Links == null || game.Links.Count == 0) && fields.Contains(MetadataField.Links))
            {
                var links = Safe(() => p.GetLinks(args))?.ToList();
                if (links != null && links.Count > 0) f.Links = links;
            }
            if (f.Community == null && game.CommunityScore == null && fields.Contains(MetadataField.CommunityScore))
                f.Community = Safe(() => p.GetCommunityScore(args));
            if (f.Critic == null && game.CriticScore == null && fields.Contains(MetadataField.CriticScore))
                f.Critic = Safe(() => p.GetCriticScore(args));
        }

        static bool StillMissing(Game game, Fetched f)
        {
            if (string.IsNullOrEmpty(game.Description) && string.IsNullOrWhiteSpace(f.Description)) return true;
            if (string.IsNullOrEmpty(game.CoverImage) && f.Cover == null) return true;
            if (string.IsNullOrEmpty(game.BackgroundImage) && f.Background == null) return true;
            if (string.IsNullOrEmpty(game.Icon) && f.Icon == null) return true;
            if (Empty(game.GenreIds) && f.Genres == null) return true;
            if (Empty(game.DeveloperIds) && f.Developers == null) return true;
            if (Empty(game.PublisherIds) && f.Publishers == null) return true;
            if (Empty(game.TagIds) && f.Tags == null) return true;
            if (Empty(game.FeatureIds) && f.Features == null) return true;
            if (game.ReleaseDate == null && f.Date == null) return true;
            return false;
        }

        static List<MetadataProperty> AsList(IEnumerable<MetadataProperty> props)
        {
            var l = props == null ? null : props.ToList();
            return l != null && l.Count > 0 ? l : null;
        }

        static Img Read(MetadataFile file)
        {
            if (file == null || !file.HasContent) return null;
            try
            {
                byte[] data = file.Content;
                string name = file.FileName ?? file.Path ?? "";
                if (data == null && !string.IsNullOrEmpty(file.Path))
                {
                    if (file.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        using (var wc = new WebClient()) data = wc.DownloadData(file.Path);
                    else if (File.Exists(file.Path))
                        data = File.ReadAllBytes(file.Path);
                }
                if (data == null || data.Length == 0) return null;
                var ext = Path.GetExtension(name.Split('?')[0]);
                if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".jpg";
                return new Img { Data = data, Ext = ext };
            }
            catch { return null; }
        }

        // ----- apply (UI thread) -----

        static bool Apply(IPlayniteAPI api, Game game, Fetched f, string tmpDir)
        {
            bool changed = false;
            string id;

            if (!string.IsNullOrWhiteSpace(f.Description) && string.IsNullOrEmpty(game.Description)) { game.Description = f.Description; changed = true; }
            if (f.Cover != null && string.IsNullOrEmpty(game.CoverImage) && (id = Save(api, f.Cover, game.Id, tmpDir)) != null) { game.CoverImage = id; changed = true; }
            if (f.Background != null && string.IsNullOrEmpty(game.BackgroundImage) && (id = Save(api, f.Background, game.Id, tmpDir)) != null) { game.BackgroundImage = id; changed = true; }
            if (f.Icon != null && string.IsNullOrEmpty(game.Icon) && (id = Save(api, f.Icon, game.Id, tmpDir)) != null) { game.Icon = id; changed = true; }

            var genres = Merge(game.GenreIds, api.Database.Genres, f.Genres);
            if (genres != null) { game.GenreIds = genres; changed = true; }
            var devs = Merge(game.DeveloperIds, api.Database.Companies, f.Developers);
            if (devs != null) { game.DeveloperIds = devs; changed = true; }
            var pubs = Merge(game.PublisherIds, api.Database.Companies, f.Publishers);
            if (pubs != null) { game.PublisherIds = pubs; changed = true; }
            var tags = Merge(game.TagIds, api.Database.Tags, f.Tags);
            if (tags != null) { game.TagIds = tags; changed = true; }
            var feats = Merge(game.FeatureIds, api.Database.Features, f.Features);
            if (feats != null) { game.FeatureIds = feats; changed = true; }
            var series = Merge(game.SeriesIds, api.Database.Series, f.Series);
            if (series != null) { game.SeriesIds = series; changed = true; }

            if (f.Date != null && game.ReleaseDate == null) { game.ReleaseDate = f.Date; changed = true; }
            if (f.Links != null && (game.Links == null || game.Links.Count == 0)) { game.Links = new ObservableCollection<Link>(f.Links); changed = true; }
            if (f.Community != null && game.CommunityScore == null) { game.CommunityScore = f.Community; changed = true; }
            if (f.Critic != null && game.CriticScore == null) { game.CriticScore = f.Critic; changed = true; }
            return changed;
        }

        static List<Guid> Merge<T>(List<Guid> current, IItemCollection<T> collection, List<MetadataProperty> props) where T : DatabaseObject
        {
            if (props == null || props.Count == 0) return null;
            var ids = current != null ? new List<Guid>(current) : new List<Guid>();
            int before = ids.Count;
            foreach (var p in props)
            {
                var byName = p as MetadataNameProperty;
                if (byName != null && !string.IsNullOrWhiteSpace(byName.Name))
                {
                    var item = collection.FirstOrDefault(x => string.Equals(x.Name, byName.Name, StringComparison.OrdinalIgnoreCase))
                               ?? collection.Add(byName.Name);
                    if (item != null && !ids.Contains(item.Id)) ids.Add(item.Id);
                    continue;
                }
                var byId = p as MetadataIdProperty;
                if (byId != null && !ids.Contains(byId.Id)) ids.Add(byId.Id);
            }
            return ids.Count > before ? ids : null;
        }

        static string Save(IPlayniteAPI api, Img img, Guid gameId, string tmpDir)
        {
            string file = null;
            try
            {
                Directory.CreateDirectory(tmpDir);
                file = Path.Combine(tmpDir, "icon_md_" + Guid.NewGuid().ToString("N") + img.Ext);
                File.WriteAllBytes(file, img.Data);
                return api.Database.AddFile(file, gameId);
            }
            catch { return null; }
            finally { try { if (file != null && File.Exists(file)) File.Delete(file); } catch { } }
        }
    }
}
