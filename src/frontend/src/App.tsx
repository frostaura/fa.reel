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
import Saved from "./pages/Saved";

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
          <Route path="/saved" element={<Saved />} />
          <Route path="/settings" element={<Settings />} />
          <Route path="/lab" element={<Lab />} />
        </Route>
        <Route path="*" element={<Navigate to="/home" replace />} />
      </Routes>
    </BrowserRouter>
  );
}
