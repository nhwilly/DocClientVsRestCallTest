# DocClientVsRestCallTest

#### This repository creates two different methods for interacting with a CosmosDb collection simulator a resource token broker application.  One approach uses the .Net DocumentDb SDK and the other makes matching REST api calls.

It uses the following steps:
1. Simulating a web server back end, it uses a master key to create document, user and permission entries for use by the clients.
2. It then returns a resource token with 'read' permissions to be used by the SDK and REST client methods.
3. When executed, the SDK works fine and the REST call fails with a 403.

