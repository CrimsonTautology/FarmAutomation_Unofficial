﻿using System;
using System.Collections.Generic;
using System.Linq;
using FarmAutomation.Common;
using FarmAutomation.ItemCollector.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Objects;
using Object = StardewValley.Object;

namespace FarmAutomation.ItemCollector.Processors
{
    internal class BuildingProcessor
    {
        private const int SlimeParentSheetIndex = 766;

        private const int PetrifiedSlimeParentSheetIndex = 557;

        private static IMonitor _monitor;

        private readonly List<string> _barnTools = new List<string>
        {
            "Milk Pail",
            "Shears"
        };

        private readonly List<string> _coopCollectibles = new List<string>
        {
            "Egg",
            "Large Egg",
            "Duck Egg",
            "Wool",
            "Duck Feather",
            "Rabbit's Foot",
            "Void Egg",
            "Dinosaur Egg"
        };

        private bool _dailiesDone;        

        public BuildingProcessor(bool petAnimals, int additionalFriendshipFromCollecting, bool muteWhenCollecting, string buildingsToIgnore, IMonitor monitor)
        {
            PetAnimals = petAnimals;
            AdditionalFriendshipFromCollecting = additionalFriendshipFromCollecting;
            MuteWhenCollecting = muteWhenCollecting;
            BuildingsToIgnore = buildingsToIgnore;
            _monitor = monitor;
        }

        public bool PetAnimals { get; set; }

        public int AdditionalFriendshipFromCollecting { get; set; }

        public bool MuteWhenCollecting { get; set; }

        public string BuildingsToIgnore { get; set; }

        public void ProcessAnimalBuildings()
        {
            if (_dailiesDone)
            {
                return;
            }

            if (MuteWhenCollecting)
            {
                SoundHelper.MuteTemporary(1500);
            }
           
            _monitor.Log("Petting animals and processing their buildings to collect items", LogLevel.Info);
            var farms = Game1.locations.OfType<Farm>();
            foreach (var farm in farms)
            {
                if (PetAnimals)
                {
                    var allAnimals =
                        farm.animals.Values.Concat(
                            farm.buildings.Where(b => b.indoors is AnimalHouse)
                                .SelectMany(i => ((AnimalHouse)i.indoors).animals.Values));
                    foreach (var animal in allAnimals)
                    {
                        PetAnimal(animal);
                    }

                    _monitor.Log("All animals have been petted.", LogLevel.Info);
                }

                foreach (var building in farm.buildings)
                {
                    var chest = ItemFinder.FindChestInLocation(building.indoors);
                    if (chest == null)
                    {
                        continue;
                    }

                    if (building is Coop && !BuildingsToIgnore.Contains(building.GetType().ToString().Split('.').Last()))
                    {
                        // collect eggs                        
                        CollectItemsFromBuilding(building, chest, _coopCollectibles);
                    }

                    if (building is Barn && !BuildingsToIgnore.Contains(building.GetType().ToString().Split('.').Last()))
                    {                        
                        var outsideAnimalCount = 0;
                        foreach (
                            var outsideAnimal in farm.animals.Values.Where(a => a.home is Barn && a.home == building))
                        {
                            CollectBarnAnimalProduce(outsideAnimal, chest);
                            ++outsideAnimalCount;
                        }

                        if (outsideAnimalCount > 0)
                        {
                            _monitor.Log(
                                $"Found {outsideAnimalCount} animals wandering outside. collected their milk or wool and put it in the chest in their {building.buildingType}");
                        }

                        var insideAnimalCount = 0;
                        foreach (var animal in ((AnimalHouse)building.indoors).animals.Values)
                        {
                            CollectBarnAnimalProduce(animal, chest);
                            ++insideAnimalCount;
                        }

                        if (insideAnimalCount > 0)
                        {                                                        
                            _monitor.Log(
                                $"Found {insideAnimalCount} animals in the {building.buildingType}. Collected their milk or wool and put it in the chest in their home.");
                        }
                    }

                    if (building.indoors is SlimeHutch && !BuildingsToIgnore.Contains(building.GetType().ToString().Split('.').Last()))
                    {                        
                        // collect goop
                        foreach (var pair in building.indoors.objects.Where(o => o.Value.Name == "Slime Ball").ToList())
                        {
                            var item = pair.Value;
                            var random =
                                new Random(
                                    (int)
                                        (Game1.stats.daysPlayed + (uint)(int)Game1.uniqueIDForThisGame +
                                         (uint)((int)item.tileLocation.X * 77) + (uint)((int)item.tileLocation.Y * 777) +
                                         2u));
                            var number = random.Next(10, 21);
                            var slimeStack = new Object(SlimeParentSheetIndex, number)
                            {
                                scale = Vector2.Zero,
                                quality = 0,
                                isSpawnedObject = false,
                                isRecipe = false,
                                questItem = false,
                                name = "Slime",
                                specialVariable = 0,
                                price = 5
                            };

                            if (random.Next(0, 4) == 0)
                            {
                                var petrifiedSlimeStack = new Object(PetrifiedSlimeParentSheetIndex, 1)
                                {
                                    scale = Vector2.Zero,
                                    quality = 0,
                                    isSpawnedObject = false,
                                    isRecipe = false,
                                    questItem = false,
                                    name = "Petrified Slime",
                                    specialVariable = 0,
                                    price = 120
                                };
                                chest.addItem(petrifiedSlimeStack);
                            }

                            if (chest.addItem(slimeStack) == null)
                            {
                                building.indoors.objects.Remove(pair.Key);
                                _monitor.Log($"Collected {number} Slimes from your Slime Hutch", LogLevel.Info);
                            }
                        }
                    }
                }
            }

            _dailiesDone = true;
        }        

        public void DailyReset()
        {
            _dailiesDone = false;
        }

        public void ProcessMachineBuildings(List<MachineBuildingConfig> buildingConfigs)
        {
            var buildings = new List<MachineBuilding>();
            var farms = Game1.locations.OfType<Farm>().ToList();
            if (buildingConfigs != null)
            {
                farms.ForEach(t =>
                {
                    var buildingsList = t.buildings.Where(b => buildingConfigs.Any(x => x.Name == b.buildingType)).ToList();
                    buildingsList.ForEach(b =>
                    {
                        var config = buildingConfigs.FirstOrDefault(x => x.Name == b.buildingType);
                        if (config == null)
                        {
                            return;
                        }

                        Chest inputChest = null;
                        if (b.GetType().GetField("input") != null)
                        {
                            inputChest = (Chest)b.GetType().GetField("input").GetValue(b);
                        }

                        Chest outputChest = null;
                        if (b.GetType().GetField("output") != null)
                        {
                            outputChest = (Chest)b.GetType().GetField("output").GetValue(b);
                        }
                
                        var machineBuilding = new MachineBuilding
                        {
                            BuildingConfig = config,
                            Input = inputChest,
                            Output = outputChest,
                            InputLocation = new Vector2(b.tileX + config.InputXOffset, b.tileY + config.InputYOffset),
                            OutputLocation = new Vector2(b.tileX + config.OutputXOffset, b.tileY + config.OutputYOffset)
                        };
                        buildings.Add(machineBuilding);
                    });
                });
            }
    
            _monitor.Log("Starting search for connected locations at Farm");
            foreach (var building in buildings)
            {
                if (building.BuildingConfig.HasInput)
                {
                    ProcessMachineBuilding(building, "IN");
                }

                if (building.BuildingConfig.HasOutput)
                {
                    ProcessMachineBuilding(building, "OUT");
                }
            }
        }

        private static void ProcessMachineBuilding(MachineBuilding building, string direction)
        {
            var connectedTileList = new List<ConnectedTile>
            {
                new ConnectedTile
                {
                    Location = (direction == "IN") ? building.InputLocation : building.OutputLocation,
                    Object = (direction == "IN") ? building.Input : building.Output
                }
            };

            ItemFinder.FindConnectedLocations(Game1.getLocationFromName("Farm"), direction == "IN" ? building.InputLocation : building.OutputLocation, connectedTileList, false);
            var connectedTile = connectedTileList.FirstOrDefault(c => c.Chest != null);
            var chest = connectedTile?.Chest;

            if (chest != null)
            {
                var items = new List<Object>();
                _monitor.Log($"{building.BuildingConfig.Name} found and chest connected.  Processing {direction.ToLower()}put.");
                try
                {
                    var i = 0;
                    switch (direction)
                    {
                        case "IN":
                            chest.items.Where(t =>
                                {
                                    if (building.BuildingConfig.AcceptableObjects != null &&
                                        building.BuildingConfig.AcceptableObjects.Any(b =>
                                        {
                                            if (b.Name != t.Name)
                                            {
                                                return b.Index == t.parentSheetIndex;
                                            }

                                            return true;
                                        }))
                                    {
                                        return true;
                                    }

                                    return building.BuildingConfig.AcceptableCategories != null && building.BuildingConfig.AcceptableCategories.Any(b => b.Index == t.category);
                                }).ToList().ForEach(x =>
                                {
                                    if (building.Input.items.Count >= 36)
                                    {
                                        return;
                                    }

                                    var item = x as Object;

                                    if (item != null)
                                    {
                                        items.Add(item);
                                        ++i;
                                    }
                                });
                            break;
                        case "OUT":
                            building.Output.items.ForEach(x =>
                            {
                                if (chest.items.Count >= 36)
                                {
                                    return;
                                }

                                var item = x as Object;

                                if (item != null)
                                {
                                    item.stack = x.Stack;
                                    {
                                        var newItem = item;
                                        newItem.stack = item.stack;
                                        items.Add(newItem);
                                        ++i;
                                    }
                                }
                            });
                            break;
                    }

                    ItemHelper.TransferBetweenInventories((direction == "IN") ? chest : building.Output, (direction == "IN") ? building.Input : chest, items);
                    _monitor.Log($"{building.BuildingConfig.Name} {direction.ToLower()}put processed: {i} stacks moved.", LogLevel.Info);
                }
                catch (Exception exception)
                {
                    _monitor.Log(exception.Message, LogLevel.Error);
                }
            }
        }

        private void CollectItemsFromBuilding(Building building, Chest chest, List<string> coopCollectibles)
        {
            var collectibles =
                building.indoors.Objects.Where(o => coopCollectibles.Contains(o.Value.name)).ToList();

            collectibles.ForEach(c =>
            {
                if (chest.addItem(c.Value) == null)
                {
                    building.indoors.Objects.Remove(c.Key);
                }

                _monitor.Log($"Collected a {c.Value.Name} and put it into the chest in your {building.buildingType}");
            });
        }

        private void PetAnimal(FarmAnimal animal)
        {
            if (!animal.wasPet)
            {
                animal.pet(Game1.player);
            }
        }

        private void CollectBarnAnimalProduce(FarmAnimal animal, Chest chest)
        {
            if (_barnTools.Contains(animal.toolUsedForHarvest))
            {                
                if (animal.currentProduce > 0 && animal.age >= animal.ageWhenMature)
                {
                    if (chest.items.Count >= 36)
                    {
                        _monitor.Log($"A {animal.type} is ready for harvesting it's produce. Unfortunately the chest in it's home is already full.", LogLevel.Error);

                        // show message that the chest is full
                        return;
                    }

                    if (animal.showDifferentTextureWhenReadyForHarvest)
                    {
                        animal.sprite.Texture = Game1.content.Load<Texture2D>("Animals\\Sheared" + animal.type);
                    }

                    chest.addItem(
                        new Object(Vector2.Zero, animal.currentProduce, null, false, true, false, false)
                        {
                            quality = animal.produceQuality
                        });
                    animal.friendshipTowardFarmer = Math.Min(1000, animal.friendshipTowardFarmer + AdditionalFriendshipFromCollecting);
                    animal.currentProduce = -1;
                }
            }
        }
    }
}
