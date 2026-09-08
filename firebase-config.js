// Carga visual opcional para la vista docente sin tocar su lógica.
// Se limita exclusivamente a docente.html y puede retirarse sin afectar Firebase.
if (typeof document !== "undefined" && /\/docente(?:-v\d+)?\.html$/i.test(window.location.pathname)) {
  const themeId = "monitor-docente-theme";
  if (!document.getElementById(themeId)) {
    const link = document.createElement("link");
    link.id = themeId;
    link.rel = "stylesheet";
    link.href = "./docente-theme.css";
    document.head.appendChild(link);
  }
}

export const firebaseConfig = {
  apiKey: "AIzaSyD2vgqvLLwcJYPc0gca2pC_ud0q31sxkXY",
  authDomain: "preciencia1.firebaseapp.com",
  databaseURL: "https://preciencia1-default-rtdb.firebaseio.com/",
  projectId: "preciencia1",
  storageBucket: "preciencia1.appspot.com",
  messagingSenderId: "1032667684315",
  appId: "1:1032667684315:web:cb64622170b3280d691bfe",
  measurementId: "G-D42XDQW2LE"
};