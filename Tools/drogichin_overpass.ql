[out:json][timeout:120];
(
  way["highway"](52.172,25.125,52.203,25.195);
  way["building"](52.172,25.125,52.203,25.195);
  way["natural"="water"](52.172,25.125,52.203,25.195);
  way["landuse"~"forest|grass|cemetery|industrial|residential"](52.172,25.125,52.203,25.195);
  way["leisure"~"park|pitch|stadium"](52.172,25.125,52.203,25.195);
);
out geom;
