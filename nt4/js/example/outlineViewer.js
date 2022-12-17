/**
 *   outlineViewer.js - Simple example of using the JS network 
 *   tables interface to produce an Outline Viewer style display 
 *   of all NT values.
 */


// Import the nt4 client as a module
import { NT4_Client, NT4_Topic } from "../src/nt4.js";

//Instantiate the new client
// using `window.location.hostname` causes the client to open a 
// NT connection on the same machine as is serving the website.
// It could be hardcoded to point at a roboRIO if needed.
var nt4Client = new NT4_Client(window.location.hostname, 
                               topicAnnounceHandler,
                               topicUnannounceHandler,
                               valueUpdateHandler,
                               onConnect,
                               onDisconnect
                               );

// Grab a reference to the HTML object where we will put values
var table = document.getElementById("mainTable");

// Create a map to remember the topics as they are announced,
// and what HTML table cell they should populate
var cellTopicIDMap = new Map();

// Allocate a variable to hold the subscription to all topics
var subscription = null;

/**
 * Topic Announce Handler
 * The NT4_Client will call this function whenever the server announces a new topic.
 * It's the user's job to react to the new topic in some useful way.
 * @param {NT4_Topic} newTopic The new topic the server just announced.
 */
function topicAnnounceHandler( newTopic ) {

    //For this example, when a new topic is announced,
    // we'll make a new row in the table for it
    var newRow = table.insertRow();
    newRow.id = newTopic.id + "_row";

    //Populate the basic, unchanging data about the topic
    newRow.insertCell(0).innerHTML = newTopic.id;
    newRow.insertCell(1).innerHTML = newTopic.name;
    newRow.insertCell(2).innerHTML = newTopic.type;

    //Create and remember the cell which displays the topic's value
    var valCell = newRow.insertCell(3);
    valCell.innerHTML = "****";
    cellTopicIDMap.set(newTopic.id, valCell);

}

/**
 * Topic UnAnnounce Handler
 * The NT4_Client will call this function whenever the server un-announces a topic.
 * It's the user's job to react to the un-anncouncement in some useful way.
 * @param {NT4_Topic} removedTopic The topic the server just un-announced.
 */
function topicUnannounceHandler( removedTopic ) {
    //For this example, when a topic is unannounced, we remove its row.
    document.getElementById(removedTopic.id + "_row").remove();
}

/**
 * Value Update Handler
 * The NT4_Client will call this function whenever the server sends a value update
 * for a topic.
 * @param {NT4_Topic} topic The topic with a value update
 * @param {double} timestamp_us The server time of the value update occurring
 * @param {*} value the new value for the topic
 */
function valueUpdateHandler( topic, timestamp_us, value ) {
    //Update shown value
    var valCell = cellTopicIDMap.get(topic.id);
    valCell.prevValue = valCell.innerHTML;
    valCell.innerHTML = value.toString().padEnd(60, '\xa0'); //This feels hacky as all getout, but prevents table vibration.

    if(valCell.prevValue !== valCell.innerHTML){
        valCell.colorDecayCounter = 25;
    }

    //update time
    document.getElementById("curTime").innerHTML = "Time: ";
    document.getElementById("curTime").innerHTML += (timestamp_us / 1000000.0).toFixed(2);
}

/**
 * On Connection Handler
 * The NT4_Client will call this after every time it successfully connects to an NT4 server.
 */
function onConnect() {

    //For this example we do a few things on connection

    //First, recreate the HTML table that will hold our values
    table.innerHTML = "";

    var titleRow = table.insertRow(0);

    var newCell;

    newCell =titleRow.insertCell(0)
    newCell.innerHTML = "<b>ID</b>";
    newCell.onclick = function() { sortTableNumeric(0); };

    newCell =titleRow.insertCell(1)
    newCell.innerHTML = "<b>Name</b>";
    newCell.onclick = function() { sortTableAlphabetic(1); };

    newCell =titleRow.insertCell(2)
    newCell.innerHTML = "<b>Type</b>";
    newCell.onclick = function() { sortTableAlphabetic(2); };

    newCell =titleRow.insertCell(3)
    newCell.innerHTML = "<b>Value</b>";

    // Then, subscribe to every topic.
    // Use a 100ms update rate (NT3 default)
    subscription = nt4Client.subscribePeriodic(["/"], 0.1);

    //Finally, update status to show we've connected.
    document.getElementById("status").innerHTML = "Connected to Server";
    
}

/**
 * On Disconnection Handler
 * The NT4_Client will call this after every time it disconnects to an NT4 server.
 */
function onDisconnect() {
    //For this example, we simply mark the status as disconnected.
    document.getElementById("status").innerHTML = "Disconnected from Server";

    //Since we've disconnected from the server, the connection is no longer valid.
    subscription = null;
}
