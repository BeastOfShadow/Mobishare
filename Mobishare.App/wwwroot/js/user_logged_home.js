document.addEventListener('DOMContentLoaded', function () {
  const form = document.getElementById('vehicleReservationForm');
  form.addEventListener('submit', function(event) {
    event.preventDefault(); // Evita il reload della pagina

    const vehicleId = document.getElementById('selectedVehicleId').value;

    fetch('?handler=ReserveVehicle', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded',
        'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value // se usi antiforgery
      },
      body: `vehicleId=${encodeURIComponent(vehicleId)}`
    })
    .then(response => response.ok ? response.text() : Promise.reject('Errore nella prenotazione'))
    .then(data => {
      const myModal = bootstrap.Modal.getInstance(document.getElementById('confirmReservationModal'));
      if (myModal) {
        myModal.hide();
      }

      const selectedId = document.getElementById('selectedVehicleId').value;
      if (vehicleMarkers[selectedId]) {
        vehicleMarkers[selectedId].setMap(null);
        delete vehicleMarkers[selectedId];
      }
    })
    .catch(error => {
      alert(error);
    });
  });
});

let map; // Mappa globale
let vehicleMarkers = {};
let marker;
window.onload = initMap;

document.addEventListener('DOMContentLoaded', function () {
  const code = document.getElementById('bikeCode');
  const btn = document.getElementById('startTravelBtn');
  const errorMessage = document.getElementById('errorMessage');

  const hub = new signalR.HubConnectionBuilder().withUrl("/VehicleHub").build();
  hub.start()
    .then(() => console.log("Connessione al SignalR Hub stabilita."))
    .catch(err => console.error(err));

  hub.on("ReceiveVehiclePositionUpdate", (vehicle) => {
    console.log("Messaggio ricevuto:", vehicle);

    const id = vehicle.vehicleId.toString();
    const newPosition = { lat: vehicle.latitude, lng: vehicle.longitude };

    //--------------------------------------------------------------------------------------------------------
    /*fetch(`/api/vehicles/${id}`, {
      method: 'GET',
      headers: {
        'Accept': 'application/json'
      }
    })
      .then(response => {
        if (!response.ok) {
          if (response.status === 404) {
            console.log("Veicolo non trovato.");
          } else {
            console.error(`Errore: ${response.status}`);
          }
          return null;
        }
        return response.json();
      })
      .then(vehicle => {
        if (vehicle) {
          console.log("Dati del veicolo:", vehicle);
        }
      })
      .catch(error => {
        console.error("Errore durante la fetch:", error);
      });*/
    /**
     * Nota per chi lo vedrà:
     * Attualmente non riesce ad eseguire la fetch su qella route, dando sempre 404.
     * Ho provato a cambiare il controller, ma non riesco a capire il perchè.
     * Se lo vedete e volete provare a dargli un'occhiata siete i benvenuti.
     * (La route è stata definita nel file GetVehicleById.cs)
     */
    //--------------------------------------------------------------------------------------------------------


    if (vehicleMarkers[id]) {
      const marker = vehicleMarkers[id];
      const currentPos = marker.getPosition();

      if (currentPos.lat() !== newPosition.lat || currentPos.lng() !== newPosition.lng) {
        marker.setPosition(newPosition);
      }
    } else {
      // Crea marker separato per ogni veicolo
      const marker = new google.maps.Marker({
        position: newPosition,
        map: map,
        title: `Veicolo ${id}`,
        icon: {
          url: "/img/vehicle/icon/bicycle.png",
          scaledSize: new google.maps.Size(40, 40)
        }
      });

      // Salva il marker in una mappa con la chiave ID
      vehicleMarkers[id] = marker;

      // Listener separato per ogni marker
      marker.addListener('click', () => {
        document.getElementById('selectedVehicleId').value = id;
        document.getElementById('vehicleIdDisplay').innerText = id;

        const myModal = new bootstrap.Modal(document.getElementById('confirmReservationModal'));
        myModal.show();
      });
    }
  });




  btn.addEventListener('click', function () {
    const vehicleCode = code.value.trim();
    if (!isNaN(vehicleCode) && vehicleCode !== "") {
      errorMessage.style.display = 'none';
      code.value = '';
    } else {
      errorMessage.style.display = 'block';
    }
  });

  code.addEventListener('input', function () {
    errorMessage.style.display = 'none';
  });

  // Salva map in una variabile accessibile al listener
  window.setMapReference = function (m) {
    map = m;
  };


});



//------------------------------------------------------------------------------------------------------

function initMap() {
  if (navigator.geolocation) {
    navigator.geolocation.getCurrentPosition(
      function (position) {
        const userLatLng = {
          lat: position.coords.latitude,
          lng: position.coords.longitude
        };

        map = new google.maps.Map(document.getElementById('map'), {
          center: userLatLng,
          zoom: 18,
          mapTypeId: 'roadmap'
        });

        const marker = new google.maps.Marker({
          position: userLatLng,
          map: map,
          title: "Sei qui!"
          /*
          icon: {
              url: "https://maps.google.com/mapfiles/ms/icons/red-dot.png", // esempio
              scaledSize: new google.maps.Size(30, 30) // opzionale: ridimensiona
          }*/
        });


        map.addListener("idle", () => {
          const bounds = map.getBounds();
          const zoom = map.getZoom();

          Object.values(vehicleMarkers).forEach(marker => {
            const isInsideBounds = bounds.contains(marker.getPosition());
            const isVisible = isInsideBounds && zoom >= 12;
            marker.setVisible(isVisible);
          });
        });


        const geocoder = new google.maps.Geocoder();
        geocoder.geocode({ location: userLatLng }, function (results, status) {
          if (status === 'OK') {
            if (results[0]) {
              locationLabel.innerText = results[0].formatted_address;
            } else {
              locationLabel.innerText = "Indirizzo non trovato.";
            }
          } else {
            console.error('Geocoder fallito per: ' + status);
            locationLabel.innerText = "Errore nel recupero dell’indirizzo.";
          }
        });
      },
      function (error) {
        document.getElementById('locationLabel').innerText = "Accesso alla posizione negato.";
        console.error("Errore geolocalizzazione:", error.message);
      }
    );
  } else {
    document.getElementById('locationLabel').innerText = "Geolocalizzazione non supportata.";
  }
}

//------------------------------------------------------------------------------------------------------
function hidePopup() {
  const popup = document.getElementById('mapPopup');
  const showBtn = document.getElementById('showPopupBtn');

  popup.style.display = 'none';
  showBtn.style.display = 'block';
}

function showPopup() {
  const popup = document.getElementById('mapPopup');

  popup.style.display = 'flex';

  startTimer();
}

async function startTimer() {
    if (typeof signalR === "undefined") {
        alert("SignalR non è stato caricato correttamente.");
        return;
    }
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/timerHub")
        .build();

    connection.on("ReceiveTime", seconds => {
        const min = Math.floor(seconds / 60);
        const sec = Math.floor(seconds % 60).toString().padStart(2, '0');
        document.getElementById("timerDisplay").textContent = `${min}:${sec}`;
    });

    connection.start()
        .then(() => connection.invoke("StartTimer"))
        .catch(err => console.error(err.toString()));
}

