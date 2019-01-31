# DocClientVsRestCallTest

#### Use case: An untrusted client application (e.g. phone) needs read only access to the customer's partition in CosmosDb so that it can directly query their own data.  The client does not have access to the DocumentDb SDK and is not running .Net.

#### This repository demonstrates two different methods for interacting with a CosmosDb collection; one approach uses the .Net DocumentDb SDK and the other makes matching REST api calls.

It uses the following steps:
1. Simulating a web server back end, it uses a master key to create document, user and permission entries for use by the clients.
2. It then returns a resource token with 'read' permissions to be used by the SDK and REST client methods.
3. When executed, the SDK works fine and the REST call fails with a 403.

