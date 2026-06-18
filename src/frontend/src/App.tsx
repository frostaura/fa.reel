import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import Layout from "./components/Layout";
import RequireAuth from "./components/RequireAuth";
import Landing from "./pages/Landing";
import TraktCallback from "./pages/TraktCallback";
import Onboarding from "./pages/Onboarding";
import Home from "./pages/Home";
import Settings from "./pages/Settings";
import Lab from "./pages/Lab";
import TitleDetail from "./pages/TitleDetail";
import Person from "./pages/Person";
import Saved from "./pages/Saved";
import TasteDna from "./pages/TasteDna";
import SearchResults from "./pages/SearchResults";
import RatePicker from "./pages/RatePicker";

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Landing />} />
        <Route path="/auth/trakt/callback" element={<TraktCallback />} />
        <Route
          path="/onboarding"
          element={
            <RequireAuth>
              <Onboarding />
            </RequireAuth>
          }
        />
        <Route
          element={
            <RequireAuth>
              <Layout />
            </RequireAuth>
          }
        >
          <Route path="/home" element={<Home />} />
          <Route path="/title/:mediaType/:tmdbId" element={<TitleDetail />} />
          <Route path="/person/:personId" element={<Person />} />
          <Route path="/saved" element={<Saved />} />
          <Route path="/dna" element={<TasteDna />} />
          <Route path="/search" element={<SearchResults />} />
          <Route path="/rate" element={<RatePicker />} />
          <Route path="/settings" element={<Settings />} />
          <Route path="/lab" element={<Lab />} />
        </Route>
        <Route path="*" element={<Navigate to="/home" replace />} />
      </Routes>
    </BrowserRouter>
  );
}
