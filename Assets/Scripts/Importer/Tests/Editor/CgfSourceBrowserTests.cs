using System.Collections.Generic;
using NUnit.Framework;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class CgfSourceBrowserTests
    {
        [Test]
        public void ApplyFilter_GroupsAndTracksSelection()
        {
            var browser = new CgfSourceBrowser();
            var allPaths = new List<string>
            {
                "objects/props/rock.cgf",
                "objects/npc/soldier.cgf",
                "objects/npc/soldier_lod1.cgf",
                "levels/test/tree.cga"
            };

            var result = browser.ApplyFilter(
                allPaths: allPaths,
                searchFilter: "npc",
                selectedPath: "objects/npc/soldier.cgf",
                autoExpandLimit: 4);

            Assert.That(result.FilteredPaths.Count, Is.EqualTo(2));
            Assert.That(result.ContainsSelectedPath, Is.True);
            Assert.That(result.GroupedPaths.ContainsKey("objects/npc"), Is.True);
            Assert.That(result.GroupedPaths["objects/npc"].Count, Is.EqualTo(2));
            Assert.That(result.SuggestedExpandedDirectories.Count, Is.EqualTo(1));
            Assert.That(result.SuggestedExpandedDirectories[0], Is.EqualTo("objects/npc"));
        }

        [Test]
        public void ApplyFilter_MarksMissingSelectionAsMissing()
        {
            var browser = new CgfSourceBrowser();
            var allPaths = new List<string>
            {
                "objects/props/rock.cgf",
                "objects/npc/soldier.cgf"
            };

            var result = browser.ApplyFilter(
                allPaths: allPaths,
                searchFilter: string.Empty,
                selectedPath: "objects/missing/not_found.cgf",
                autoExpandLimit: 2);

            Assert.That(result.FilteredPaths.Count, Is.EqualTo(2));
            Assert.That(result.ContainsSelectedPath, Is.False);
        }

        [Test]
        public void ParseSelection_WhenPathIsMissing_ReturnsErrorInsteadOfThrowing()
        {
            var browser = new CgfSourceBrowser();
            string missingPath = "objects/missing/not_found.cgf";

            var result = browser.ParseSelection(missingPath);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParsedPath, Is.EqualTo(missingPath));
            Assert.That(result.ParsedFile, Is.Null);
            Assert.That(result.ParseError, Is.Not.Null.And.Not.Empty);
            Assert.That(result.SiblingLodPaths, Is.Not.Null);
        }
    }
}
