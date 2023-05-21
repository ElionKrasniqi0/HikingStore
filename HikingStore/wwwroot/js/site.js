// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
//location int the details

document.getElementById("locmap").contentWindow.onerror = function () {
var e = document.getElementById('locmap');
var d = document.createElement('div');
d.innerHTML = e.innerHTML;
d.innerHTML = '<div class="text-danger text-center mt-5" style="font-size:25px; padding:10px;">Location could not be loaded at the moment!</div>';
e.parentNode.replaceChild(d, e);
}
